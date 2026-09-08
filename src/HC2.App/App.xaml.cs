using System.Windows;
using HC2.App.ViewModels;
using HC2.Core;

namespace HC2.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var viewModel = new MainWindowViewModel(new SerialPortScanner());

        MainWindow = new MainWindow(viewModel);
        MainWindow.Show();
    }
}
