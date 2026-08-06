using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NasaApodApp.ViewModels;

// Base for every view model. WPF's binding engine listens to PropertyChanged and refreshes
// the bound control, so a view model never touches a control directly.
public abstract class VmBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    // Passing null tells WPF that every bound property on this object may have changed.
    protected void OnPropertyChanged([CallerMemberName] string? PropertyName = null)
        => this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(PropertyName));

    // Stores the value only when it differs, then raises the change notification. The return
    // value lets a caller chain follow-up work, such as re-evaluating a command's enabled state.
    protected bool SetProperty<T>(ref T Field, T Value, [CallerMemberName] string? PropertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(Field, Value))
        {
            return false;
        }

        Field = Value;
        this.OnPropertyChanged(PropertyName);
        return true;
    }
}
