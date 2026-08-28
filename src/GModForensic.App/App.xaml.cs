using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using GModForensic.Abstractions;
using GModForensic.App.Services;
using GModForensic.App.Views;
using GModForensic.Detection;
using GModForensic.Native.Io;
using GModForensic.Presentation;
using GModForensic.Presentation.Demo;
using GModForensic.Presentation.Services;

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

        var defaults = new ScanConfiguration();

        var session = new ScanSession(
            new DemoModuleProvider(),
            new WindowsCapabilityProvider(),
            new FileSystemFactsProvider(defaults.MaxFileSizeForHashBytes));

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        // Regle de demonstration de l'etape 4 : elle alimente l'ecran de resultats en attendant
        // le moteur reel (etapes 6 et 7). Ses identifiants sont prefixes DEMO.
        var engine = new DetectionEngine([new DemoDetectionRule()]);

        var shell = new ShellViewModel(session, new ReportExporter(version), engine);
        shell.Export.BrowseRequested = FolderPicker.Pick;

        MainWindow = new ShellWindow(shell);
        MainWindow.Show();
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
