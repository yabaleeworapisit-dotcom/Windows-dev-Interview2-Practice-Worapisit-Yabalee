using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;
using NasaApodApp.Common;

namespace NasaApodLib;

// Downloads the picture behind an entry's address and hands back a frozen BitmapImage.
//
// Two reasons this does not simply point a WPF Image control at the address: a bound
// BitmapImage downloads on the UI thread and stutters the window, and it gives no way to
// report a broken address back to the user. Downloading the bytes here keeps the wait off
// the UI thread and turns a failure into a value the view model can react to.
//
// Pictures already fetched are kept in memory, so revisiting a slide is instant. A background
// worker reads ahead through a queue of days the user has not reached yet, so stepping is
// usually instant too. A download already running is shared rather than started again, so the
// same file is never fetched twice however the two paths overlap.
public sealed class ApodPictureLoader : IDisposable
{
    // Wider than the slide area ever gets on a normal display, so nothing visible is lost,
    // while a 6000-pixel original still stops costing what a 6000-pixel original costs.
    private const int MaxDecodeWidth = 1600;

    private readonly HttpClient httpClient;
    private readonly ConcurrentDictionary<string, BitmapImage> loadedPictures = new(StringComparer.Ordinal);

    // Downloads currently running, keyed by address. A second request for an address already
    // being fetched joins the running one instead of opening its own.
    private readonly ConcurrentDictionary<string, Task<BitmapImage?>> runningDownloads = new(StringComparer.Ordinal);

    // The read-ahead queue and the set of addresses already in it. The set is what stops the
    // same day being queued again every time the window slides forward by one.
    private readonly ConcurrentQueue<string> prefetchQueue = new();
    private readonly HashSet<string> queuedUrls = new(StringComparer.Ordinal);
    private readonly Lock queueLock = new();

    private CancellationTokenSource prefetchCancel = new();
    private Task prefetchWorker = Task.CompletedTask;

    public ApodPictureLoader()
    {
        this.httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60),
        };
    }

    // Returns null when the address is empty, the caller gave up waiting, or the picture
    // cannot be fetched. A missing picture is a normal outcome (video entries, retired links),
    // not an error worth throwing.
    public async Task<BitmapImage?> LoadPictureAsync(string PictureUrl, CancellationToken CancelToken)
    {
        if (string.IsNullOrWhiteSpace(PictureUrl))
        {
            return null;
        }

        if (this.loadedPictures.TryGetValue(PictureUrl, out BitmapImage? CachedPicture))
        {
            return CachedPicture;
        }

        // The download itself is not tied to this caller's token. Two things follow: the same
        // file is only ever fetched once no matter how many callers want it, and a caller who
        // walks away does not throw away bytes already paid for — the download finishes and
        // lands in the cache for whoever arrives next.
        Task<BitmapImage?> SharedDownload = this.runningDownloads.GetOrAdd(
            PictureUrl,
            Address => this.DownloadAndCacheAsync(Address));

        if (!CancelToken.CanBeCanceled)
        {
            return await SharedDownload.ConfigureAwait(false);
        }

        TaskCompletionSource CallerGaveUp = new();
        using (CancelToken.Register(() => CallerGaveUp.TrySetResult()))
        {
            Task Winner = await Task.WhenAny(SharedDownload, CallerGaveUp.Task).ConfigureAwait(false);
            if (Winner != SharedDownload)
            {
                // This caller moved on. The download carries on for everyone else.
                return null;
            }
        }

        return await SharedDownload.ConfigureAwait(false);
    }

    // Adds days the user has not reached yet to the read-ahead queue, skipping any already
    // held or already waiting. One worker drains the queue one picture at a time — deliberately
    // not in parallel, because twenty simultaneous downloads would take bandwidth away from
    // the picture the user is actually waiting to see.
    public void QueuePrefetch(IEnumerable<string> PictureUrls)
    {
        ArgumentNullException.ThrowIfNull(PictureUrls);

        int AddedCount = 0;
        lock (this.queueLock)
        {
            foreach (string PictureUrl in PictureUrls)
            {
                if (string.IsNullOrWhiteSpace(PictureUrl)
                    || this.loadedPictures.ContainsKey(PictureUrl)
                    || !this.queuedUrls.Add(PictureUrl))
                {
                    continue;
                }

                this.prefetchQueue.Enqueue(PictureUrl);
                AddedCount++;
            }

            if (AddedCount > 0)
            {
                LayerLogger.Log(
                    ApiId.LoadPicture,
                    Layers.Service,
                    Layers.Service,
                    $"read-ahead queued {AddedCount} picture(s), {this.prefetchQueue.Count} waiting");
            }

            this.EnsureWorkerRunning();
        }
    }

    // Called when a new span is requested: whatever was being read ahead belongs to the span
    // the user has just left, so it is abandoned rather than left competing for bandwidth with
    // the new one.
    //
    // Pictures already downloaded stay cached — they cost nothing to keep. A download already
    // in flight is also left to finish: it is one file, and stopping it would throw away bytes
    // that are nearly paid for.
    public void CancelPrefetch()
    {
        lock (this.queueLock)
        {
            this.prefetchCancel.Cancel();
            this.prefetchCancel.Dispose();
            this.prefetchCancel = new CancellationTokenSource();

            int DroppedCount = this.prefetchQueue.Count;
            this.prefetchQueue.Clear();
            this.queuedUrls.Clear();

            if (DroppedCount > 0)
            {
                LayerLogger.Log(
                    ApiId.LoadPicture,
                    Layers.Service,
                    Layers.Service,
                    $"read-ahead cancelled, {DroppedCount} queued picture(s) dropped");
            }
        }
    }

    public void Dispose()
    {
        this.CancelPrefetch();
        this.prefetchCancel.Dispose();
        this.httpClient.Dispose();
    }

    // Freezing matters: the bytes are decoded on a background thread, and only a frozen
    // BitmapImage may cross to the UI thread that binds it.
    //
    // DecodePixelWidth caps the decode at roughly the width the slide is ever given. A picture
    // wider than that is scaled down while decoding rather than after, which skips building
    // the full-size bitmap in memory — several times less work and memory on a large original.
    private static BitmapImage Bytes2Bitmap(byte[] PictureBytes)
    {
        using MemoryStream PictureStream = new(PictureBytes);

        BitmapImage RetPicture = new();
        RetPicture.BeginInit();
        RetPicture.CacheOption = BitmapCacheOption.OnLoad;
        RetPicture.DecodePixelWidth = MaxDecodeWidth;
        RetPicture.StreamSource = PictureStream;
        RetPicture.EndInit();
        RetPicture.Freeze();

        return RetPicture;
    }

    // The one place bytes are actually fetched. Everything else joins this through
    // runningDownloads, which is what guarantees one download per address.
    private async Task<BitmapImage?> DownloadAndCacheAsync(string PictureUrl)
    {
        try
        {
            LayerLogger.Log(ApiId.LoadPicture, Layers.Service, Layers.NasaApi, $"GET picture {PictureUrl}");

            byte[] PictureBytes = await this.httpClient
                .GetByteArrayAsync(new Uri(PictureUrl))
                .ConfigureAwait(false);

            BitmapImage DecodedPicture = Bytes2Bitmap(PictureBytes);

            LayerLogger.Log(
                ApiId.LoadPicture,
                Layers.NasaApi,
                Layers.Service,
                $"decoded {PictureBytes.Length} bytes -> {DecodedPicture.PixelWidth}x{DecodedPicture.PixelHeight}");

            this.loadedPictures[PictureUrl] = DecodedPicture;
            return DecodedPicture;
        }
        catch (HttpRequestException NetworkFailure)
        {
            LayerLogger.Log(ApiId.LoadPicture, Layers.NasaApi, Layers.Service, $"[ERROR] {NetworkFailure.Message}");
            return null;
        }
        catch (TaskCanceledException)
        {
            // The 60 second timeout elapsed.
            return null;
        }
        catch (NotSupportedException DecodeFailure)
        {
            // The bytes came back but are not a picture format WPF can decode.
            LayerLogger.Log(ApiId.LoadPicture, Layers.NasaApi, Layers.Service, $"[ERROR] {DecodeFailure.Message}");
            return null;
        }
        finally
        {
            // Removed once settled, so a later failure can be retried rather than replaying a
            // stale null forever.
            this.runningDownloads.TryRemove(PictureUrl, out _);
        }
    }

    // Must be called with queueLock held. Starts a worker only when the previous one has
    // finished, so there is never more than one read-ahead download in flight.
    private void EnsureWorkerRunning()
    {
        if (!this.prefetchWorker.IsCompleted)
        {
            return;
        }

        CancellationToken WorkerToken = this.prefetchCancel.Token;
        this.prefetchWorker = Task.Run(() => this.DrainPrefetchQueueAsync(WorkerToken), WorkerToken);
    }

    private async Task DrainPrefetchQueueAsync(CancellationToken CancelToken)
    {
        while (!CancelToken.IsCancellationRequested && this.prefetchQueue.TryDequeue(out string? NextUrl))
        {
            if (this.loadedPictures.ContainsKey(NextUrl))
            {
                continue;
            }

            try
            {
                await this.LoadPictureAsync(NextUrl, CancelToken).ConfigureAwait(false);
            }
            catch (Exception PrefetchFailure)
            {
                // Read-ahead is speculative. A failure here is not the user's problem: if they
                // ever reach this day, the real load will report it then.
                LayerLogger.Log(ApiId.LoadPicture, Layers.NasaApi, Layers.Service, $"[read-ahead skipped] {PrefetchFailure.Message}");
            }
        }
    }
}
