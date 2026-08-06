using System.Windows.Input;

namespace NasaApodApp.ViewModels;

// An ICommand backed by plain delegates, for actions that finish immediately — stepping to
// the next slide, opening the detail screen, going back.
public sealed class RelayCmd : ICommand
{
    private readonly Action execute;
    private readonly Func<bool>? canExecute;

    public RelayCmd(Action Execute, Func<bool>? CanExecute = null)
    {
        ArgumentNullException.ThrowIfNull(Execute);

        this.execute = Execute;
        this.canExecute = CanExecute;
    }

    // Routing through CommandManager means bound buttons re-check their enabled state on the
    // same cadence WPF already uses, with no extra plumbing in the views.
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => this.canExecute?.Invoke() ?? true;

    public void Execute(object? parameter)
    {
        if (!this.CanExecute(parameter))
        {
            return;
        }

        this.execute();
    }
}
