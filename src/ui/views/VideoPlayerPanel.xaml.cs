using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace NasaApodApp.Views;

// A self-contained player for the days whose entry is a video file: play and pause, a bar to
// scrub through the clip, and looping when it reaches the end.
//
// MediaElement is a control, not something a view model can drive, so the transport logic
// lives here rather than being forced through bindings. Keeping it in its own control is what
// stops that code from landing in the slide screen, which stays a picture browser.
public partial class VideoPlayerPanel : UserControl
{
    public static readonly DependencyProperty VideoSourceProperty = DependencyProperty.Register(
        nameof(VideoSource),
        typeof(Uri),
        typeof(VideoPlayerPanel),
        new PropertyMetadata(null, OnVideoSourceChanged));

    // Drives the scrub bar while the clip plays. Fast enough to look continuous, slow enough
    // that it is not doing meaningful work.
    private readonly DispatcherTimer positionTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    // While the grip is held the bar belongs to the user, so the timer must not fight them
    // for it by writing the playback position back into it.
    private bool isUserScrubbing;

    private bool isPlaying;

    public VideoPlayerPanel()
    {
        this.InitializeComponent();

        this.positionTimer.Tick += this.OnPositionTick;
        this.Unloaded += (_, _) => this.StopAndRelease();
    }

    // The clip to play. Null means this day has no video, and the player releases whatever it
    // was holding — otherwise a clip would keep playing under the next slide.
    public Uri? VideoSource
    {
        get => (Uri?)this.GetValue(VideoSourceProperty);
        set => this.SetValue(VideoSourceProperty, value);
    }

    private static void OnVideoSourceChanged(DependencyObject Target, DependencyPropertyChangedEventArgs Args)
    {
        VideoPlayerPanel Panel = (VideoPlayerPanel)Target;
        Uri? NewSource = Args.NewValue as Uri;

        if (NewSource is null)
        {
            Panel.StopAndRelease();
            return;
        }

        Panel.Player.Source = NewSource;
        Panel.ScrubSlider.Value = 0;
        Panel.TimeLabel.Text = "0:00 / 0:00";
        Panel.StartPlaying();
    }

    private static string Duration2Text(TimeSpan Value)
        => string.Format(
            CultureInfo.InvariantCulture,
            "{0}:{1:00}",
            (int)Value.TotalMinutes,
            Value.Seconds);

    // NaturalDuration is only known once the file has been opened, so the scrub bar cannot be
    // given its range any earlier than this.
    private void OnMediaOpened(object sender, RoutedEventArgs e)
    {
        if (!this.Player.NaturalDuration.HasTimeSpan)
        {
            // A stream of unknown length cannot be scrubbed; leave the bar disabled.
            this.ScrubSlider.IsEnabled = false;
            return;
        }

        this.ScrubSlider.IsEnabled = true;
        this.ScrubSlider.Maximum = this.Player.NaturalDuration.TimeSpan.TotalSeconds;
        this.UpdateTimeLabel();
    }

    // Looping: rewind and carry on rather than freezing on the last frame.
    private void OnMediaEnded(object sender, RoutedEventArgs e)
    {
        this.Player.Position = TimeSpan.Zero;
        this.Player.Play();
    }

    private void OnMediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        this.positionTimer.Stop();
        this.isPlaying = false;
        this.PlayPauseButton.Content = "▶";
        this.TimeLabel.Text = "cannot play";
    }

    private void OnPlayPauseClicked(object sender, RoutedEventArgs e)
    {
        if (this.isPlaying)
        {
            this.Player.Pause();
            this.positionTimer.Stop();
            this.isPlaying = false;
            this.PlayPauseButton.Content = "▶";
            return;
        }

        this.StartPlaying();
    }

    private void OnRestartClicked(object sender, RoutedEventArgs e)
    {
        this.Player.Position = TimeSpan.Zero;
        this.ScrubSlider.Value = 0;
        this.StartPlaying();
    }

    private void OnScrubStarted(object sender, DragStartedEventArgs e) => this.isUserScrubbing = true;

    private void OnScrubFinished(object sender, DragCompletedEventArgs e)
    {
        this.SeekToSliderPosition();
        this.isUserScrubbing = false;
    }

    // IsMoveToPointEnabled lets a plain click jump the grip, which raises no drag events —
    // so the click has to seek on its own.
    private void OnScrubClicked(object sender, MouseButtonEventArgs e)
    {
        if (!this.isUserScrubbing)
        {
            this.SeekToSliderPosition();
        }
    }

    private void SeekToSliderPosition()
    {
        if (this.Player.Source is null)
        {
            return;
        }

        this.Player.Position = TimeSpan.FromSeconds(this.ScrubSlider.Value);
        this.UpdateTimeLabel();
    }

    private void StartPlaying()
    {
        this.Player.Play();
        this.positionTimer.Start();
        this.isPlaying = true;
        this.PlayPauseButton.Content = "⏸";
    }

    private void StopAndRelease()
    {
        this.positionTimer.Stop();
        this.isPlaying = false;

        this.Player.Stop();
        this.Player.Close();
        this.Player.Source = null;

        this.PlayPauseButton.Content = "▶";
        this.ScrubSlider.Value = 0;
        this.TimeLabel.Text = "0:00 / 0:00";
    }

    private void OnPositionTick(object? sender, EventArgs e)
    {
        if (this.isUserScrubbing || !this.Player.NaturalDuration.HasTimeSpan)
        {
            return;
        }

        this.ScrubSlider.Value = this.Player.Position.TotalSeconds;
        this.UpdateTimeLabel();
    }

    private void UpdateTimeLabel()
    {
        TimeSpan Total = this.Player.NaturalDuration.HasTimeSpan
            ? this.Player.NaturalDuration.TimeSpan
            : TimeSpan.Zero;

        this.TimeLabel.Text = $"{Duration2Text(this.Player.Position)} / {Duration2Text(Total)}";
    }
}
