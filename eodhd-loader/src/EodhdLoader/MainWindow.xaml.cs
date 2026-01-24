using System.Windows;
using EodhdLoader.ViewModels;

namespace EodhdLoader;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Auto-refresh dashboard on load
        Loaded += async (s, e) =>
        {
            if (viewModel.Dashboard.RefreshCommand.CanExecute(null))
            {
                await viewModel.Dashboard.RefreshCommand.ExecuteAsync(null);
            }
        };
    }
}
