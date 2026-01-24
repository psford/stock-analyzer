using CommunityToolkit.Mvvm.ComponentModel;

namespace EodhdLoader.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private BorisViewModel _boris;

    [ObservableProperty]
    private IndexManagerViewModel _indexManager;

    [ObservableProperty]
    private DashboardViewModel _dashboard;

    [ObservableProperty]
    private LoaderViewModel _loader;

    [ObservableProperty]
    private MigrationViewModel _migration;

    [ObservableProperty]
    private int _selectedTabIndex;

    public MainViewModel(
        BorisViewModel boris,
        IndexManagerViewModel indexManager,
        DashboardViewModel dashboard,
        LoaderViewModel loader,
        MigrationViewModel migration)
    {
        _boris = boris;
        _indexManager = indexManager;
        _dashboard = dashboard;
        _loader = loader;
        _migration = migration;
    }
}
