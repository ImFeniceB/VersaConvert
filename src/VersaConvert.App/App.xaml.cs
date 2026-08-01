using System.Windows;

namespace VersaConvert.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            System.Windows.MessageBox.Show(
                args.Exception.Message,
                "VersaConvert — errore inatteso",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        base.OnStartup(e);
    }
}
