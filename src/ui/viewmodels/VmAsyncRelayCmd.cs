using System.Windows.Input;

namespace NasaApodApp.ViewModels;

// An ICommand for work that awaits input and output — fetching a month from NASA. While the
// awaited work runs the command reports itself disabled, which both blocks a second click and
// greys the bound button out without any code in the view.
public sealed class AsyncRelayCmd : ICommand
{
    private readonly Func<Task> execute;
    private readonly Func<bool>? canExecute;
    private bool isRunning;

    public AsyncRelayCmd(Func<Task> Execute, Func<bool>? CanExecute = null)
    {
        ArgumentNullException.ThrowIfNull(Execute);

        this.execute = Execute;
        this.canExecute = CanExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter)
        => !this.isRunning && (this.canExecute?.Invoke() ?? true);

    // Execute is async void because that is the signature ICommand exposes. Anything thrown
    // past this point would take the process down, so the awaited work is responsible for
    // handling its own failures — VmSlideBrowser.FetchMonthAsync does exactly that.
    public async void Execute(object? parameter)
    {
        if (!this.CanExecute(parameter))
        {
            return;
        }

        this.isRunning = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await this.execute();
        }
        finally
        {
            this.isRunning = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
