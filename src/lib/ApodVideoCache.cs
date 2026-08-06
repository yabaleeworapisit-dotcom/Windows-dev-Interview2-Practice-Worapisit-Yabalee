using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net.Http;
using NasaApodApp.Common;

namespace NasaApodLib;

// Downloads a day's video to a file on disk and hands back its path.
//
// MediaElement is not given the address directly. Every APOD video address is https, plain
// http redirects to it, and MediaElement's streaming does not reliably survive either — the
// player reports a failure and shows nothing. Reading the file first sidesteps the question
// entirely, and has a second benefit: seeking within a local file is instant, where seeking a
// stream on a slow link means waiting for it to buffer again.
//
// The cost is that playback cannot start until the file has arrived, so the download reports
// progress and the screen shows it.
public sealed class ApodVideoCache : IDisposable
{
    private readonly HttpClient httpClient;
    private readonly ConcurrentDictionary<string, string> savedFiles = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task<string?>> runningDownloads = new(StringComparer.Ordinal);
    private readonly string cacheFolder;
    private readonly Lock cancelLock = new();

    // Aborts the transfer itself, not merely the wait for it. A video is tens of megabytes and
    // will saturate a slow link for a minute or more; leaving one running after the user has
    // moved on starves whatever they asked for next.
    private CancellationTokenSource downloadCancel = new();

    public ApodVideoCache()
    {
        this.httpClient = new HttpClient
        {
            // Long, because these files run to tens of megabytes on a slow link.
            Timeout = TimeSpan.FromMinutes(10),
        };

        this.cacheFolder = Path.Combine(Path.GetTempPath(), "NasaApodVideoCache");
        Directory.CreateDirectory(this.cacheFolder);
    }

    // Returns the path to a playable local copy, or null when the video could not be fetched.
    // Progress runs from 0 to 1 while the bytes arrive.
    public async Task<string?> GetLocalFileAsync(string VideoUrl, IProgress<double>? ProgressReport, CancellationToken CancelToken)
    {
        if (string.IsNullOrWhiteSpace(VideoUrl))
        {
            return null;
        }

        if (this.savedFiles.TryGetValue(VideoUrl, out string? AlreadySaved) && File.Exists(AlreadySaved))
        {
            ProgressReport?.Report(1);
            return AlreadySaved;
        }

        // Same rule as pictures: one download per address however many callers want it.
        Task<string?> SharedDownload = this.runningDownloads.GetOrAdd(
            VideoUrl,
            Address => this.DownloadToFileAsync(Address, ProgressReport));

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
                // The user moved to another day. The file keeps downloading for next time.
                return null;
            }
        }

        return await SharedDownload.ConfigureAwait(false);
    }

    // Called when the user asks for a different span: the video belongs to the span they have
    // just left. Unlike the picture read-ahead, which lets its one in-flight file finish
    // because it is nearly paid for, this really does stop the transfer — the sizes are not
    // comparable, and a half-finished video is worth nothing anyway.
    public void CancelDownloads()
    {
        lock (this.cancelLock)
        {
            if (this.runningDownloads.IsEmpty)
            {
                return;
            }

            LayerLogger.Log(
                ApiId.LoadVideo,
                Layers.Service,
                Layers.Service,
                $"video download cancelled, {this.runningDownloads.Count} transfer(s) stopped");

            this.downloadCancel.Cancel();
            this.downloadCancel.Dispose();
            this.downloadCancel = new CancellationTokenSource();
        }
    }

    public void Dispose()
    {
        this.downloadCancel.Cancel();
        this.downloadCancel.Dispose();
        this.httpClient.Dispose();

        // The cached videos are large and of no use once the program has closed.
        try
        {
            if (Directory.Exists(this.cacheFolder))
            {
                Directory.Delete(this.cacheFolder, recursive: true);
            }
        }
        catch (IOException)
        {
            // A file still held by the player cannot be removed. Windows clears its own
            // temporary folder eventually, so this is not worth reporting.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // Keeps the day and the original file name, so the cache folder is readable if anyone
    // looks inside it.
    private static string Url2FileName(string VideoUrl)
    {
        string LastSegment = new Uri(VideoUrl).Segments.LastOrDefault() ?? "video.mp4";
        string SafeName = string.Concat(LastSegment.Split(Path.GetInvalidFileNameChars()));

        return SafeName.Length > 0 ? SafeName : "video.mp4";
    }

    private async Task<string?> DownloadToFileAsync(string VideoUrl, IProgress<double>? ProgressReport)
    {
        string TargetPath = Path.Combine(this.cacheFolder, Url2FileName(VideoUrl));

        CancellationToken TransferToken;
        lock (this.cancelLock)
        {
            TransferToken = this.downloadCancel.Token;
        }

        try
        {
            LayerLogger.Log(ApiId.LoadVideo, Layers.Service, Layers.NasaApi, $"GET video {VideoUrl}");

            using HttpResponseMessage Response = await this.httpClient
                .GetAsync(new Uri(VideoUrl), HttpCompletionOption.ResponseHeadersRead, TransferToken)
                .ConfigureAwait(false);

            Response.EnsureSuccessStatusCode();

            long? TotalBytes = Response.Content.Headers.ContentLength;
            await CopyToFileAsync(Response, TargetPath, TotalBytes, ProgressReport, TransferToken).ConfigureAwait(false);

            LayerLogger.Log(
                ApiId.LoadVideo,
                Layers.NasaApi,
                Layers.Service,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "video saved: {0} bytes -> {1}",
                    new FileInfo(TargetPath).Length,
                    TargetPath));

            this.savedFiles[VideoUrl] = TargetPath;
            ProgressReport?.Report(1);
            return TargetPath;
        }
        catch (HttpRequestException NetworkFailure)
        {
            LayerLogger.Log(ApiId.LoadVideo, Layers.NasaApi, Layers.Service, $"[ERROR] {NetworkFailure.Message}");
            return null;
        }
        catch (IOException WriteFailure)
        {
            LayerLogger.Log(ApiId.LoadVideo, Layers.NasaApi, Layers.Service, $"[ERROR] {WriteFailure.Message}");
            return null;
        }
        catch (OperationCanceledException)
        {
            // Abandoned partway through. The part-written file would look like a whole one to
            // the next run, so it goes.
            DeleteIfPresent(TargetPath);
            return null;
        }
        finally
        {
            // Removed once settled, so a failure can be retried rather than replaying a stale
            // null forever.
            this.runningDownloads.TryRemove(VideoUrl, out _);
        }
    }

    private static void DeleteIfPresent(string Path)
    {
        try
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static async Task CopyToFileAsync(
        HttpResponseMessage Response,
        string TargetPath,
        long? TotalBytes,
        IProgress<double>? ProgressReport,
        CancellationToken CancelToken)
    {
        using Stream Incoming = await Response.Content.ReadAsStreamAsync(CancelToken).ConfigureAwait(false);
        using FileStream Outgoing = new(TargetPath, FileMode.Create, FileAccess.Write, FileShare.Read);

        byte[] Buffer = new byte[81920];
        long WrittenBytes = 0;
        int ReadCount;

        while ((ReadCount = await Incoming.ReadAsync(Buffer, CancelToken).ConfigureAwait(false)) > 0)
        {
            await Outgoing.WriteAsync(Buffer.AsMemory(0, ReadCount), CancelToken).ConfigureAwait(false);
            WrittenBytes += ReadCount;

            if (TotalBytes is > 0)
            {
                ProgressReport?.Report((double)WrittenBytes / TotalBytes.Value);
            }
        }
    }
}
