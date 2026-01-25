using CommunityToolkit.Mvvm.ComponentModel;

namespace EodhdLoader.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private BorisViewModel _boris;

    [ObservableProperty]
    private CrawlerViewModel _crawler;

    [ObservableProperty]
    private IndexManagerViewModel _indexManager;

    [ObservableProperty]
    private DashboardViewModel _dashboard;

    [ObservableProperty]
    private int _selectedTabIndex;

    public MainViewModel(
        BorisViewModel boris,
        CrawlerViewModel crawler,
        IndexManagerViewModel indexManager,
        DashboardViewModel dashboard)
    {
        _boris = boris;
        _crawler = crawler;
        _indexManager = indexManager;
        _dashboard = dashboard;
    }
}
