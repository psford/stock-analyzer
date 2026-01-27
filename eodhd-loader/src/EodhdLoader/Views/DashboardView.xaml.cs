using System.Windows;
using System.Windows.Controls;
using EodhdLoader.ViewModels;

namespace EodhdLoader.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Auto-refresh when the tab is first shown
        if (DataContext is DashboardViewModel vm && vm.Stats == null && !vm.IsBusy)
        {
            try
            {
                vm.RefreshCommand.Execute(null);
            }
            catch
            {
                // Swallow - the ViewModel handles its own error display
            }
        }
    }
}
