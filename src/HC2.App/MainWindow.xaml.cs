using System;
using System.Windows;
using HC2.App.Interop;
using HC2.App.ViewModels;

namespace HC2.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    private DeviceChangeNotifier? _deviceChanges;

    public MainWindow(MainWindowViewModel viewModel)
    {
        _viewModel  = viewModel;
        DataContext = viewModel;

        InitializeComponent();
    }

    protected override async void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _deviceChanges                =  new DeviceChangeNotifier(this);
        _deviceChanges.DeviceChanged  += (_, _) => _viewModel.OnDeviceChanged();

        await _viewModel.RefreshAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _deviceChanges?.Dispose();

        base.OnClosed(e);
    }
}
