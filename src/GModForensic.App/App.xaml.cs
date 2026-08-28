using System.Windows;
using System.Windows.Threading;

namespace GModForensic.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // §25 — dernier filet : dans le pire des cas l'outil reste utilisable et exportable,
        // il ne disparait jamais silencieusement.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ShowFatal(args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
            ShowFatal(args.Exception);
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        ShowFatal(e.Exception);
    }

    private static void ShowFatal(Exception? exception) =>
        MessageBox.Show(
            $"Une erreur inattendue est survenue.\n\n{exception?.Message}\n\n"
            + "Le scan en cours peut etre exporte depuis l'ecran de resultats.",
            "GMod Forensic Scanner",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
}
