using System.Windows;
using EodhdLoader.ViewModels;

namespace EodhdLoader;

public partial class CrawlerWindow : Window
{
    public CrawlerWindow(CrawlerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
