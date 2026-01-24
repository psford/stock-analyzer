using CommunityToolkit.Mvvm.ComponentModel;

namespace EodhdLoader.ViewModels;

/// <summary>
/// Base class for all ViewModels providing common functionality.
/// Uses CommunityToolkit.Mvvm for INPC implementation.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    protected void SetStatus(string message)
    {
        StatusMessage = message;
    }
}
