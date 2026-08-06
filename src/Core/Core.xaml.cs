using System.Windows;
using NasaApodApp.Models;
using NasaApodApp.ViewModels;
using NasaApodLib;

namespace NasaApodApp;

// Application entry point and the one place the object graph is assembled: read the API key,
// build the API client and the picture loader, hand both to the shell view model, show the
// window. Nothing below this point constructs its own dependencies.
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ApodApiSettings ApiSettings = ApodApiSettings.LoadFromAppFolder();

        ApodApiClient ApiClient = new(ApiSettings.ApiKey);
        ApodPictureLoader PictureLoader = new();
        ApodVideoCache VideoCache = new();
        VmMain ShellViewModel = new(ApiClient, PictureLoader, VideoCache, ApiSettings.IsDemoKey);

        // The objects above live as long as the window does. Releasing them from the Exit
        // handler rather than from fields keeps this class free of disposable state, which
        // nothing would ever be in a position to dispose.
        this.Exit += (_, _) =>
        {
            ShellViewModel.Dispose();
            VideoCache.Dispose();
            PictureLoader.Dispose();
            ApiClient.Dispose();
        };

        // Fully qualified: inside an Application subclass the bare name MainWindow also names
        // the inherited Application.MainWindow property.
        Views.MainWindow ShellWindow = new() { DataContext = ShellViewModel };
        ShellWindow.Show();
    }
}
