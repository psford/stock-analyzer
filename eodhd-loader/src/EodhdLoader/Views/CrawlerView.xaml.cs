using System.Windows;
using System.Windows.Controls;
using EodhdLoader.ViewModels;

namespace EodhdLoader.Views;

public partial class CrawlerView : UserControl
{
    private CrawlerWindow? _popOutWindow;

    public CrawlerView()
    {
        InitializeComponent();
    }

    private void PopOutButton_Click(object sender, RoutedEventArgs e)
    {
        if (_popOutWindow != null && _popOutWindow.IsVisible)
        {
            _popOutWindow.Activate();
            return;
        }

        if (DataContext is CrawlerViewModel viewModel)
        {
            _popOutWindow = new CrawlerWindow(viewModel);
            _popOutWindow.Closed += (s, args) => _popOutWindow = null;
            _popOutWindow.Show();
        }
    }
}
