# Practice Windows Dev (C# / .NET 10)

This project was written for an interview2 and practice with **Persec Co., Ltd.**
- Interview date:  06/08/2026/15:16
- Due date before: 06/08/2026/23:59

## Project Structure

```
Build/                                     # -- BUILD -- project and compiler settings
|-- NasaApod.slnx                          #   solution opened by Visual Studio
|-- NasaApod.csproj                        #   project: output type, icon, source and XAML items
|-- Common.props                           #   compiler and analyser settings, imported by the project
`-- NasaApod.exe                           #   optional self-contained build (see Run Project)

src/                                       # -- SOURCE -- C# files
|--- config/                               #   settings read at run time
|    `-- ApodApiConfig.json                #     the NASA API key; excluded from version control
|
|--- Core/                                 # -- STARTUP --
|    |-- Core.xaml                         #     application resources: palette, control styles, templates
|    `-- Core.xaml.cs                      #     entry point; constructs every service once and releases them on exit
|
|--- common/                               # -- SHARED --
|    `-- LayerLogger.cs                    #     one log format used by every layer
|
|--- lib/                                  # -- LOGIC -- listed in the order data flows
|    |-- ApodDateRange.cs                  #     converts the chosen months into start_date and end_date
|    |-- ApodApiClient.cs                  #     issues the HTTP request and reports failures in plain words
|    |-- ApodJsonReader.cs                 #     deserialises the response body into ApodEntry[]
|    |-- ApodPictureLoader.cs              #     downloads and decodes images; caches them and reads ahead
|    |-- ApodSlideNavigator.cs             #     tracks the selected day and the rules for moving between days
|    `-- ApodVideoCache.cs                 #     downloads a video to disk so the player can open it
|
|---models/                                # -- DATA --
|    |-- ApodEntry.cs                      #     one day's entry as returned by the API
|    `-- ApodApiSettings.cs                #     the API key, loaded from config/
|
|---ui/                                    # -- UI files --
     |--- viewmodels/                      #   state and commands; no view model references a control
     |   |-- VmAsyncRelayCmd.cs            #     ICommand for awaited work; disables itself while running
     |   |-- VmBase.cs                     #     INotifyPropertyChanged base used by every view model
     |   |-- VmMain.cs                     #     window shell; owns the browser and its dependencies
     |   |-- VmRelayCmd.cs                 #     ICommand for immediate actions
     |   `-- VmSlideBrowser.cs             #     the screen's state: date selection, day list, media, detail panel
     |
     `---views/                            #   XAML, plus the code a control genuinely requires
         |-- MainWindow.xaml               #     window frame: title, icon, size, theme
         |-- MainWindow.xaml.cs            #     code-behind; initialisation only
         |-- SlideBrowserView.xaml         #     the whole screen: pickers, day list, media area, detail panel
         |-- SlideBrowserView.xaml.cs      #     keeps the selected row scrolled into view
         |-- VideoPlayerPanel.xaml         #     video surface and its transport bar
         |-- VideoPlayerPanel.xaml.cs      #     play, pause, seek and looping
         `-- VisibilityConverters.cs       #     bind a bool to Visibility, in either direction
```

## Environment
+------------------+---------------------------------------+
| Item             | Version                               |
|------------------+---------------------------------------|
| OS               | Windows 11                            |
| .NET SDK         | 10.0.302                              |
| .NET Runtime     | Microsoft.WindowsDesktop.App 10.0.10  |
| Target Framework | net10.0-windows                       |
| Language         | C# 14                                 |
| NuGet packages   | none — framework only                 | 
+------------------+---------------------------------------+

`Build/Common.props` holds the settings, so the project file itself stays about what the
project *is* rather than how it should be compiled:

+-----------------------------+----------------------+-----------------------------------------------------------------+
| Setting                     | Value                | Detail                                                          |
|-----------------------------|----------------------|-----------------------------------------------------------------|
| `Nullable`                  | `enable`             | Warn when a value that may be null is used unchecked            |
| `ImplicitUsings`            | `enable`             | Import the common namespaces automatically                      |
| `EnableNETAnalyzers`        | `true`               | Run the .NET code analysers on every build                      |
| `AnalysisLevel`             | `latest-recommended` | Use the current recommended set of analyser rules               |
| `EnforceCodeStyleInBuild`   | `true`               | Report style violations at build time, not only in the editor   |
| `TreatWarningsAsErrors`     | `true`               | Fail the build on any warning, so none can be left behind       |
| `EnableDefaultCompileItems` | `false`              | Do not auto-discover files; they are listed in the project      |
+-----------------------------+----------------------+-----------------------------------------------------------------+

## Run Project
1. **Visual Studio** — open `Build/NasaApod.slnx`, then Rebuild and Run (F5).
2. **Command line**, from the repository root:
   ```powershell
   dotnet build Build/NasaApod.csproj
   dotnet run --project Build/NasaApod.csproj
   ```
3. **Run executable file**, in `Build/NasaApod.exe`, which runs on a Windows x64 machine with no
   .NET installed. Remember to copy `ApodApiConfig.json` next to it.

### How to use this program?

1. **Select Start date** — a month and a year. The request always begins on the 1st of it.
2. **Select End date** — likewise. The request always ends on the last day of that month.
   *Note:* the start month cannot be later than the end month, and any picker left empty is
   outlined in red when you press the button.
3. **Click Get APOD.** The line above the button shows the exact range that will be requested,
   for example `Get date: 01-11-2021 to 30-11-2021`.
4. **Wait for the response.** Wait for response data in the range arrives of one request and 
   appears in the list on the left.
   *(Timeout: 60 sec. If it expires, the reason is shown and anything already loaded stays on
   screen — press Get APOD again.)*
5. **Browse.** Choose a day in the list, or use `First` / `< Previous` / `Next >` / `Last`, or
   the arrow keys. **Show Detail** opens the panel on the right with every field the API
   returned; its values can be selected and copied.
6. Video use case and Debug Log attached in TestResult folder. 


Hot keys
 `←` `→` : previous / next day
 `Home` `End` : first / last day 
 `Enter` `Esc` : open / close the Detail panel 

## Limitation

**The archive starts on 16 June 1995** and has nothing for tomorrow. A range reaching past
either end is trimmed rather than refused — June 1995 requests the 16th to the 30th.

**Videos are downloaded before they play.** WPF's `MediaElement` will not reliably stream from
the `https` addresses APOD uses, so the file is fetched to a temporary folder first. For a
17.8 MB clip that is a wait of roughly a minute on a slow connection, shown as a percentage.
Seeking is then instant, which streaming would not have been. The temporary folder is deleted
when the program closes.

**Some video days show nothing.** NASA generates a thumbnail for embedded videos (YouTube,
Vimeo) but not for the video files it hosts itself. A day of the first kind shows its
thumbnail; a day of the second plays in place. A day that is embedded *and* has no thumbnail
can only offer its address.

**`api.nasa.gov` is sometimes unavailable.** It returns 503 from time to time regardless of the
request. When a call fails the days already loaded stay on screen with the reason above them —
pressing Get APOD again is usually enough.

**Timeouts.** Three, sized to what each transfer actually is:
The API call  : **60 sec** 
One picture   : **60 sec** 
One video     : **10 min**  These run to tens of megabytes. `Eruption_SDO.mp4` is 17.8 MB and took 60 seconds on the connection this was written on.

## Credits

**Images and data** come from NASA's
[Astronomy Picture of the Day](https://apod.nasa.gov/apod/astropix.html) through the
[NASA Open APIs](https://api.nasa.gov). Entries returned without a `copyright` field are public
domain; the rest belong to the photographers named in the Detail panel.