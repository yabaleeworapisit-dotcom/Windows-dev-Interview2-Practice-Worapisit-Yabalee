using System.Windows.Controls;

namespace NasaApodApp.Views;

public partial class SlideBrowserView : UserControl
{
    public SlideBrowserView()
    {
        this.InitializeComponent();

        // The arrow-key bindings only fire once this control holds focus, and it is not
        // focusable by tabbing into anything on first show.
        this.Loaded += (_, _) => this.Focus();
    }

    // Keeps the highlighted day visible. Stepping with the Next and Previous buttons moves the
    // selection in the list, but a list does not scroll itself to follow a selection it was
    // not clicked for, so the chosen day would otherwise drift off the top or bottom.
    private void OnDaySelected(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox List && List.SelectedItem is not null)
        {
            List.ScrollIntoView(List.SelectedItem);
        }
    }
}
