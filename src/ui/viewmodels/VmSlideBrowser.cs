using System.Globalization;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using NasaApodApp.Models;
using NasaApodLib;

namespace NasaApodApp.ViewModels;

// A chosen span of months, shown a day at a time.
//
// The user picks a start month and an end month; the request always runs from the first day
// of the start month to the last day of the end month. The whole span is fetched in one
// ranged call, and the days are then stepped through one slide at a time. The detail panel
// on the right holds every field the response carried for the day on screen.
public sealed class VmSlideBrowser : VmBase, IDisposable
{
    // How many days ahead of the visible slide are downloaded before the user asks for them.
    // Enough that continuous stepping stays ahead of the reader on a slow link, while still
    // being a bounded amount of work that a new span can cleanly abandon.
    private const int PrefetchAheadCount = 20;

    // And how many behind. Shallower on purpose: stepping back usually finds a day already
    // held from walking past it, so this only has to cover days skipped over in a hurry.
    private const int PrefetchBehindCount = 5;

    private readonly ApodApiClient apiClient;
    private readonly ApodPictureLoader pictureLoader;
    private readonly ApodVideoCache videoCache;
    private readonly ApodSlideNavigator slideNavigator = new();
    private readonly bool isDemoKey;

    // Cancels the picture download for a slide the user has already stepped away from,
    // so a slow picture can never overwrite a newer one.
    private CancellationTokenSource? pictureCancel;

    // The same, for the video file of a day the user has stepped away from.
    private CancellationTokenSource? videoCancel;

    private Uri? currentVideoUri;
    private bool isVideoDownloading;
    private double videoDownloadFraction;

    // All four start empty on purpose: the user must state both months, and an empty picker
    // is what the missing-field highlight is there to point at.
    private MonthChoice? startMonth;
    private int? startYear;
    private MonthChoice? endMonth;
    private int? endYear;

    // Set the first time Get APOD is pressed. Until then the pickers stay unmarked, so a
    // window that has only just opened is not covered in red before anyone has done anything.
    private bool hasAttemptedFetch;

    private BitmapImage? currentPicture;
    private bool isFetching;
    private bool isPictureLoading;
    private bool isDetailOpen;
    private string statusMessage = string.Empty;
    private string errorMessage = string.Empty;

    public VmSlideBrowser(ApodApiClient ApiClient, ApodPictureLoader PictureLoader, ApodVideoCache VideoCache, bool IsDemoKey)
    {
        ArgumentNullException.ThrowIfNull(ApiClient);
        ArgumentNullException.ThrowIfNull(PictureLoader);
        ArgumentNullException.ThrowIfNull(VideoCache);

        this.apiClient = ApiClient;
        this.pictureLoader = PictureLoader;
        this.videoCache = VideoCache;
        this.isDemoKey = IsDemoKey;

        this.SelectableYears = ApodDateRange.SelectableYears();
        this.SelectableMonths = BuildMonthChoices();

        this.FetchMonthCommand = new AsyncRelayCmd(this.FetchSpanAsync);
        this.MoveNextCommand = new RelayCmd(this.MoveNext, () => this.slideNavigator.CanMoveNext);
        this.MovePreviousCommand = new RelayCmd(this.MovePrevious, () => this.slideNavigator.CanMovePrevious);
        this.MoveFirstCommand = new RelayCmd(this.MoveFirst, () => this.slideNavigator.CanMovePrevious);
        this.MoveLastCommand = new RelayCmd(this.MoveLast, () => this.slideNavigator.CanMoveNext);
        this.ToggleDetailCommand = new RelayCmd(this.ToggleDetail, () => this.CurrentEntry is not null);

        this.statusMessage = this.isDemoKey
            ? "Running on the shared DEMO_KEY — 30 requests an hour, 50 a day, and often slow. "
              + "Put a personal key from api.nasa.gov into ApodApiConfig.json for 4,000 an hour."
            : "Choose a start month and an end month, then press Get APOD.";
    }

    public int[] SelectableYears { get; }

    public MonthChoice[] SelectableMonths { get; }

    public MonthChoice? StartMonth
    {
        get => this.startMonth;
        set
        {
            if (this.SetProperty(ref this.startMonth, value))
            {
                this.RefreshDateSelection();
            }
        }
    }

    public int? StartYear
    {
        get => this.startYear;
        set
        {
            if (this.SetProperty(ref this.startYear, value))
            {
                this.RefreshDateSelection();
            }
        }
    }

    public MonthChoice? EndMonth
    {
        get => this.endMonth;
        set
        {
            if (this.SetProperty(ref this.endMonth, value))
            {
                this.RefreshDateSelection();
            }
        }
    }

    public int? EndYear
    {
        get => this.endYear;
        set
        {
            if (this.SetProperty(ref this.endYear, value))
            {
                this.RefreshDateSelection();
            }
        }
    }

    // Each of these puts a red border on one picker. They stay false until Get APOD has been
    // pressed at least once, and clear again the moment that picker is filled in.
    public bool IsStartMonthMissing => this.hasAttemptedFetch && this.startMonth is null;

    public bool IsStartYearMissing => this.hasAttemptedFetch && this.startYear is null;

    public bool IsEndMonthMissing => this.hasAttemptedFetch && this.endMonth is null;

    public bool IsEndYearMissing => this.hasAttemptedFetch && this.endYear is null;

    public bool HasMissingDate
        => this.IsStartMonthMissing || this.IsStartYearMissing || this.IsEndMonthMissing || this.IsEndYearMissing;

    public bool IsDateSelectionComplete
        => this.startMonth is not null && this.startYear is not null
        && this.endMonth is not null && this.endYear is not null;

    // Writes back the exact span the next request will cover, in the day-first form the
    // pickers imply: "Get date: 01-11-2021 to 30-11-2021".
    public string RequestRangeText
    {
        get
        {
            if (!this.TryReadSelectedSpan(out DateOnly StartDate, out DateOnly EndDate, out _))
            {
                return string.Empty;
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "Get date: {0} to {1}",
                ApodDateRange.Date2DisplayStr(StartDate),
                ApodDateRange.Date2DisplayStr(EndDate));
        }
    }

    public ApodEntry? CurrentEntry => this.slideNavigator.CurrentEntry;

    public BitmapImage? CurrentPicture
    {
        get => this.currentPicture;
        private set => this.SetProperty(ref this.currentPicture, value);
    }

    public bool IsFetching
    {
        get => this.isFetching;
        private set => this.SetProperty(ref this.isFetching, value);
    }

    public bool IsPictureLoading
    {
        get => this.isPictureLoading;
        private set => this.SetProperty(ref this.isPictureLoading, value);
    }

    public bool IsDetailOpen
    {
        get => this.isDetailOpen;
        private set
        {
            if (this.SetProperty(ref this.isDetailOpen, value))
            {
                this.OnPropertyChanged(nameof(this.CanOfferDetail));
            }
        }
    }

    // Shows the button that opens the detail panel. It steps aside once the panel is open,
    // because the panel then carries its own Hide control in its heading — two controls for
    // the same thing, both on screen at once, is one too many.
    public bool CanOfferDetail => this.HasSlides && !this.isDetailOpen;

    public string StatusMessage
    {
        get => this.statusMessage;
        private set => this.SetProperty(ref this.statusMessage, value);
    }

    public string ErrorMessage
    {
        get => this.errorMessage;
        private set => this.SetProperty(ref this.errorMessage, value);
    }

    public bool HasError => this.errorMessage.Length > 0;

    public bool HasSlides => this.slideNavigator.HasSlides;

    // Reads "Day 4 of 30" under the picture.
    public string SlidePositionText
        => this.slideNavigator.HasSlides
            ? string.Format(
                CultureInfo.InvariantCulture,
                "Day {0} of {1}",
                this.slideNavigator.CurrentPosition,
                this.slideNavigator.SlideCount)
            : string.Empty;

    // Drives the badge laid over a video day's thumbnail, so a still frame is never mistaken
    // for that day's actual picture.
    public bool IsCurrentEntryVideo => this.CurrentEntry is not null && !this.CurrentEntry.IsImage;

    // A video NASA hosts as a file of its own is played in place rather than shown as a still,
    // because no thumbnail exists for it to show.
    public bool IsCurrentEntryPlayable => this.CurrentEntry?.IsPlayableVideo ?? false;

    // The local copy, not the web address: see ApodVideoCache for why the player is never
    // handed the address directly. Null until the file has finished arriving.
    public Uri? CurrentVideoUri
    {
        get => this.currentVideoUri;
        private set => this.SetProperty(ref this.currentVideoUri, value);
    }

    public bool IsVideoDownloading
    {
        get => this.isVideoDownloading;
        private set => this.SetProperty(ref this.isVideoDownloading, value);
    }

    public double VideoDownloadPercent => this.videoDownloadFraction * 100;

    public string VideoDownloadText
        => string.Format(CultureInfo.InvariantCulture, "Fetching the video ... {0:0}%", this.VideoDownloadPercent);

    // Every day in the loaded span, for the list beside the slide.
    public IReadOnlyList<ApodEntry> Entries => this.slideNavigator.Entries;

    // Two-way with the list: picking a day there moves the slide, and stepping with the
    // buttons moves the highlight back. Both go through the navigator, so there is only ever
    // one idea of which day is current.
    public ApodEntry? SelectedEntry
    {
        get => this.CurrentEntry;
        set
        {
            if (this.slideNavigator.JumpToEntry(value))
            {
                this.OnSlideChanged();
            }
        }
    }

    // True only when the day has nothing to draw and nothing to play — an embedded video whose
    // thumbnail NASA did not supply.
    public bool IsCurrentEntryBlank => this.CurrentEntry?.HasNothingToShow ?? false;

    public ICommand FetchMonthCommand { get; }

    public ICommand MoveNextCommand { get; }

    public ICommand MovePreviousCommand { get; }

    public ICommand MoveFirstCommand { get; }

    public ICommand MoveLastCommand { get; }

    public ICommand ToggleDetailCommand { get; }

    public void Dispose()
    {
        this.pictureCancel?.Cancel();
        this.pictureCancel?.Dispose();
        this.pictureCancel = null;

        this.videoCancel?.Cancel();
        this.videoCancel?.Dispose();
        this.videoCancel = null;
    }

    private static MonthChoice[] BuildMonthChoices()
    {
        string[] MonthNames = CultureInfo.InvariantCulture.DateTimeFormat.MonthNames;

        MonthChoice[] RetChoices = new MonthChoice[12];
        for (int MonthNumber = 1; MonthNumber <= 12; MonthNumber++)
        {
            RetChoices[MonthNumber - 1] = new MonthChoice(MonthNumber, MonthNames[MonthNumber - 1]);
        }

        return RetChoices;
    }

    // The single place the four pickers turn into a pair of dates. Returns false while any
    // picker is still empty, which is what keeps RequestRangeText blank until it can be true.
    private bool TryReadSelectedSpan(out DateOnly StartDate, out DateOnly EndDate, out string Problem)
    {
        StartDate = default;
        EndDate = default;
        Problem = string.Empty;

        if (!this.IsDateSelectionComplete)
        {
            return false;
        }

        return ApodDateRange.TryBuildSpan(
            this.startYear!.Value,
            this.startMonth!.Number,
            this.endYear!.Value,
            this.endMonth!.Number,
            out StartDate,
            out EndDate,
            out Problem);
    }

    private async Task FetchSpanAsync()
    {
        // Pressing the button is what turns the missing-field highlights on; from here they
        // track the pickers live, clearing as each one is filled.
        this.hasAttemptedFetch = true;
        this.RefreshMissingDateFlags();

        this.ErrorMessage = string.Empty;
        this.OnPropertyChanged(nameof(this.HasError));

        if (!this.IsDateSelectionComplete)
        {
            // No message is set here on purpose. The red borders and the single "Please select
            // date" line beside the button already say it; putting it in ErrorMessage as well
            // printed the same sentence twice on screen.
            return;
        }

        if (!this.TryReadSelectedSpan(out DateOnly StartDate, out DateOnly EndDate, out string SpanProblem))
        {
            this.ShowError(SpanProblem);
            return;
        }

        // Whatever was still downloading belongs to the span the user has just left. Dropping
        // it here frees the connection for the span they actually asked for.
        //
        // The video matters most: it is tens of megabytes against the API's tens of kilobytes,
        // and one left running was enough to push the request past its timeout. Measured on
        // this link, the same call took 3.5 seconds alone and 37 seconds beside a video.
        this.pictureLoader.CancelPrefetch();
        this.CancelVideoDownload();

        this.IsFetching = true;
        this.StatusMessage = string.Format(
            CultureInfo.InvariantCulture,
            "Loading {0} to {1} ...",
            ApodDateRange.Date2DisplayStr(StartDate),
            ApodDateRange.Date2DisplayStr(EndDate));

        try
        {
            string ResponseBody = await this.apiClient
                .FetchRangeJsonAsync(StartDate, EndDate, CancellationToken.None)
                .ConfigureAwait(true);

            ApodEntry[] FetchedEntries = ApodJsonReader.Json2Entries(ResponseBody);

            this.slideNavigator.LoadSlides(FetchedEntries);
            this.RefreshSlideBoundProperties();

            if (FetchedEntries.Length == 0)
            {
                this.StatusMessage = "NASA returned no pictures for that range.";
                this.CurrentPicture = null;
                return;
            }

            this.StatusMessage = string.Format(
                CultureInfo.InvariantCulture,
                "{0} pictures loaded for {1} to {2}.",
                FetchedEntries.Length,
                ApodDateRange.Date2DisplayStr(StartDate),
                ApodDateRange.Date2DisplayStr(EndDate));

            // Started, not awaited. Awaiting it here would keep this command "running" until
            // the first picture had downloaded, and an async command reports itself disabled
            // while it runs — so Get APOD stayed greyed out for the whole download and a new
            // span could not be requested. The spinner covers the wait instead.
            _ = this.LoadCurrentPictureAsync();
            _ = this.LoadCurrentVideoAsync();
        }
        catch (ApodApiException ApiFailure)
        {
            // The message on this exception is already written for the user.
            //
            // Nothing is cleared here. A failed request says nothing about the days already on
            // screen, and throwing them away turned a request that did not arrive into an
            // application that appeared to have broken: the list emptied, the picture vanished,
            // and the detail panel sat there showing field names with no values. The days the
            // user was reading stay exactly where they were, with the reason shown above them.
            this.ShowError(ApiFailure.Message);
        }
        finally
        {
            this.IsFetching = false;
        }
    }

    private void MoveNext()
    {
        if (this.slideNavigator.MoveNext())
        {
            this.OnSlideChanged();
        }
    }

    private void MovePrevious()
    {
        if (this.slideNavigator.MovePrevious())
        {
            this.OnSlideChanged();
        }
    }

    private void MoveFirst()
    {
        if (this.slideNavigator.MoveFirst())
        {
            this.OnSlideChanged();
        }
    }

    private void MoveLast()
    {
        if (this.slideNavigator.MoveLast())
        {
            this.OnSlideChanged();
        }
    }

    private void ToggleDetail() => this.IsDetailOpen = !this.IsDetailOpen;

    private void OnSlideChanged()
    {
        this.RefreshSlideBoundProperties();

        // Fire and forget on purpose: stepping to the next slide must feel instant, and the
        // media fills in when it arrives. Both loaders swallow their own failures.
        _ = this.LoadCurrentPictureAsync();
        _ = this.LoadCurrentVideoAsync();
    }

    // A video day needs its file on disk before the player can open it, which on these file
    // sizes is a wait worth showing progress for.
    private async Task LoadCurrentVideoAsync()
    {
        ApodEntry? ShownEntry = this.CurrentEntry;

        this.videoCancel?.Cancel();
        this.videoCancel?.Dispose();
        this.videoCancel = null;

        this.CurrentVideoUri = null;
        this.SetVideoProgress(0);

        if (ShownEntry is null || !ShownEntry.IsPlayableVideo)
        {
            this.IsVideoDownloading = false;
            return;
        }

        CancellationTokenSource ThisLoadCancel = new();
        this.videoCancel = ThisLoadCancel;
        this.IsVideoDownloading = true;

        try
        {
            Progress<double> ProgressReport = new(Fraction =>
            {
                if (ReferenceEquals(this.videoCancel, ThisLoadCancel))
                {
                    this.SetVideoProgress(Fraction);
                }
            });

            string? LocalPath = await this.videoCache
                .GetLocalFileAsync(ShownEntry.Url, ProgressReport, ThisLoadCancel.Token)
                .ConfigureAwait(true);

            // The user may have stepped elsewhere while the file was arriving.
            if (ThisLoadCancel.IsCancellationRequested || !ReferenceEquals(this.CurrentEntry, ShownEntry))
            {
                return;
            }

            if (LocalPath is null)
            {
                this.StatusMessage = "The video for this day could not be downloaded.";
                return;
            }

            this.CurrentVideoUri = new Uri(LocalPath);
        }
        finally
        {
            if (ReferenceEquals(this.videoCancel, ThisLoadCancel))
            {
                this.IsVideoDownloading = false;
            }
        }
    }

    // Stops both halves: the wait for the file, and the transfer feeding it.
    private void CancelVideoDownload()
    {
        this.videoCancel?.Cancel();
        this.videoCancel?.Dispose();
        this.videoCancel = null;

        this.videoCache.CancelDownloads();

        this.IsVideoDownloading = false;
        this.SetVideoProgress(0);
    }

    private void SetVideoProgress(double Fraction)
    {
        this.videoDownloadFraction = Fraction;
        this.OnPropertyChanged(nameof(this.VideoDownloadPercent));
        this.OnPropertyChanged(nameof(this.VideoDownloadText));
    }

    private async Task LoadCurrentPictureAsync()
    {
        ApodEntry? ShownEntry = this.CurrentEntry;

        this.pictureCancel?.Cancel();
        this.pictureCancel?.Dispose();
        this.pictureCancel = null;

        // A video day still has a picture to show — its thumbnail. Only a day with nothing
        // drawable at all skips the download.
        //
        // IsPictureLoading has to be cleared here as well as in the finally below. Leaving it
        // alone was a real defect: the finally only clears the flag when its own load is still
        // the current one, so stepping from a picture onto a day with none left "Loading
        // picture ..." on screen for good, overlapping whatever the new slide drew.
        if (ShownEntry is null || !ShownEntry.HasPicture)
        {
            this.CurrentPicture = null;
            this.IsPictureLoading = false;

            // Still worth warming the days either side. A video day is exactly when there is
            // spare time to do it, since nothing is being downloaded for this slide.
            this.PrefetchAround();
            return;
        }

        CancellationTokenSource ThisLoadCancel = new();
        this.pictureCancel = ThisLoadCancel;

        this.CurrentPicture = null;
        this.IsPictureLoading = true;

        try
        {
            BitmapImage? LoadedPicture = await this.pictureLoader
                .LoadPictureAsync(ShownEntry.DisplayUrl, ThisLoadCancel.Token)
                .ConfigureAwait(true);

            // The user may have stepped to another slide while this download was in flight;
            // in that case the newer load owns the screen and this result is dropped.
            if (ThisLoadCancel.IsCancellationRequested || !ReferenceEquals(this.CurrentEntry, ShownEntry))
            {
                return;
            }

            this.CurrentPicture = LoadedPicture;

            if (LoadedPicture is null)
            {
                this.StatusMessage = "The picture for this day could not be downloaded.";
            }

            this.PrefetchAround();
        }
        finally
        {
            if (ReferenceEquals(this.pictureCancel, ThisLoadCancel))
            {
                this.IsPictureLoading = false;
            }
        }
    }

    // Queues the days around the current slide so stepping lands on something already in
    // memory. Queued after the visible picture, never before, so read-ahead cannot delay what
    // the user is waiting for.
    //
    // Forward first, and deeper, because that is the direction of travel. Backwards is queued
    // after and shallower: those days are usually cached already from walking past them, and
    // the window only earns its keep when the user skipped over them quickly or jumped with
    // Home and End.
    private void PrefetchAround()
    {
        int CurrentIndex = this.slideNavigator.CurrentIndex;

        List<string> Wanted = [];
        CollectPictureUrls(Wanted, CurrentIndex, Step: 1, Count: PrefetchAheadCount);
        CollectPictureUrls(Wanted, CurrentIndex, Step: -1, Count: PrefetchBehindCount);

        if (Wanted.Count > 0)
        {
            this.pictureLoader.QueuePrefetch(Wanted);
        }
    }

    private void CollectPictureUrls(List<string> Into, int FromIndex, int Step, int Count)
    {
        for (int Moved = 1; Moved <= Count; Moved++)
        {
            ApodEntry? Entry = this.slideNavigator.EntryAt(FromIndex + (Step * Moved));
            if (Entry is null)
            {
                return;
            }

            if (Entry.HasPicture)
            {
                Into.Add(Entry.DisplayUrl);
            }
        }
    }

    private void ShowError(string Message)
    {
        this.ErrorMessage = Message;
        this.StatusMessage = string.Empty;
        this.OnPropertyChanged(nameof(this.HasError));
    }

    // Called whenever one of the four pickers changes: the written range follows the new
    // selection, and a highlight on the picker just filled in goes away.
    private void RefreshDateSelection()
    {
        this.OnPropertyChanged(nameof(this.RequestRangeText));
        this.OnPropertyChanged(nameof(this.IsDateSelectionComplete));
        this.RefreshMissingDateFlags();
    }

    private void RefreshMissingDateFlags()
    {
        this.OnPropertyChanged(nameof(this.IsStartMonthMissing));
        this.OnPropertyChanged(nameof(this.IsStartYearMissing));
        this.OnPropertyChanged(nameof(this.IsEndMonthMissing));
        this.OnPropertyChanged(nameof(this.IsEndYearMissing));
        this.OnPropertyChanged(nameof(this.HasMissingDate));
    }

    // Every property that reads through the navigator has to be re-announced together,
    // because the navigator itself raises no notifications.
    private void RefreshSlideBoundProperties()
    {
        this.OnPropertyChanged(nameof(this.Entries));
        this.OnPropertyChanged(nameof(this.CurrentEntry));
        this.OnPropertyChanged(nameof(this.SelectedEntry));
        this.OnPropertyChanged(nameof(this.HasSlides));
        this.OnPropertyChanged(nameof(this.CanOfferDetail));
        this.OnPropertyChanged(nameof(this.SlidePositionText));
        this.OnPropertyChanged(nameof(this.IsCurrentEntryVideo));
        this.OnPropertyChanged(nameof(this.IsCurrentEntryPlayable));
        this.OnPropertyChanged(nameof(this.CurrentVideoUri));
        this.OnPropertyChanged(nameof(this.IsCurrentEntryBlank));
        CommandManager.InvalidateRequerySuggested();
    }
}

// A month in a picker: the number the API needs, and the name a person reads.
public sealed class MonthChoice
{
    public MonthChoice(int Number, string Name)
    {
        this.Number = Number;
        this.Name = Name;
    }

    public int Number { get; }

    public string Name { get; }

    // A safety net. The combo box draws this object through an ItemTemplate, but anything
    // that falls back to the plain text of the object still reads as the month name rather
    // than a type name.
    public override string ToString() => this.Name;
}
