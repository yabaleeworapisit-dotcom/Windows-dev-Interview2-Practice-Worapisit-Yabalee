using NasaApodLib;

namespace NasaApodApp.ViewModels;

// The window shell. It owns the browser screen and the lifetime of everything the browser
// holds, so App.xaml.cs has one object to dispose rather than a handful.
//
// There is no screen switching here: the list of days, the day on screen and its full detail
// are three panels of one screen, not pages to navigate between.
public sealed class VmMain : VmBase, IDisposable
{
    public VmMain(ApodApiClient ApiClient, ApodPictureLoader PictureLoader, ApodVideoCache VideoCache, bool IsDemoKey)
    {
        ArgumentNullException.ThrowIfNull(ApiClient);
        ArgumentNullException.ThrowIfNull(PictureLoader);
        ArgumentNullException.ThrowIfNull(VideoCache);

        this.SlideBrowser = new VmSlideBrowser(ApiClient, PictureLoader, VideoCache, IsDemoKey);
    }

    public VmSlideBrowser SlideBrowser { get; }

    public void Dispose() => this.SlideBrowser.Dispose();
}
