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
    private ShellViewModel? _shell;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        StartupLog.Write("--- Demarrage ---");

        // §25 — dernier filet une fois l'interface visible. Pendant le demarrage lui-meme,
        // une exception est traitee plus bas : la masquer laisserait un processus sans
        // fenetre, visible seulement dans le gestionnaire des taches.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            StartupLog.Write("Exception non geree", (args.ExceptionObject as Exception)!);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
            StartupLog.Write("Tache non observee", args.Exception);
        };

        try
        {
            ShowShell();
        }
        catch (Exception ex)
        {
            // Echec avant l'affichage : on le dit, on le journalise, et on s'arrete.
            // Un processus vivant sans fenetre serait pire que pas de programme du tout.
            StartupLog.Write("Echec du demarrage", ex);

            MessageBox.Show(
                $"Le programme n'a pas pu demarrer.\n\n{ex.GetType().Name} : {ex.Message}\n\n"
                + $"Details enregistres dans :\n{StartupLog.Path ?? "(journal indisponible)"}",
                "GMod Forensic Scanner",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
        }
    }

    private void ShowShell()
    {
        var defaults = new ScanConfiguration();

        StartupLog.Write("Construction de la session");

        // Aucune mesure de privileges ici : elle a lieu apres l'affichage de la fenetre.
        var session = new ScanSession(
            new DemoModuleProvider(),
            new WindowsCapabilityProvider(),
            new FileSystemFactsProvider(defaults.MaxFileSizeForHashBytes));

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        // Regle de demonstration de l'etape 4 : elle alimente l'ecran de resultats en attendant
        // le moteur reel (etapes 6 et 7). Ses identifiants sont prefixes DEMO.
        var engine = new DetectionEngine([new DemoDetectionRule()]);

        _shell = new ShellViewModel(session, new ReportExporter(version), engine);
        _shell.Export.BrowseRequested = FolderPicker.Pick;

        StartupLog.Write("Construction de la fenetre");
        var window = new ShellWindow(_shell);

        MainWindow = window;
        window.Show();
        window.Activate();

        StartupLog.Write("Fenetre affichee");

        // La mesure des privileges sonde le jeton, les volumes et le dossier Prefetch.
        // Elle demarre seulement maintenant, pour ne jamais retarder l'affichage.
        _ = InitializeShellAsync();
    }

    private async Task InitializeShellAsync()
    {
        try
        {
            StartupLog.Write("Mesure des privileges");
            await _shell!.InitializeAsync().ConfigureAwait(true);
            StartupLog.Write("Privileges mesures");
        }
        catch (Exception ex)
        {
            // L'interface est deja visible : on informe sans jamais fermer le programme.
            StartupLog.Write("Echec de la mesure des privileges", ex);

            MessageBox.Show(
                $"Les privileges n'ont pas pu etre mesures.\n\n{ex.Message}\n\n"
                + "Les verifications qui en dependent seront annoncees comme indisponibles.",
                "GMod Forensic Scanner",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        StartupLog.Write("Exception d'interface", e.Exception);

        // Une fenetre existe : on peut absorber l'erreur sans perdre le scan en cours.
        // Sans fenetre, absorber reviendrait a laisser un processus fantome.
        if (MainWindow is null)
        {
            return;
        }

        e.Handled = true;

        MessageBox.Show(
            $"Une erreur inattendue est survenue.\n\n{e.Exception.Message}\n\n"
            + "Le scan en cours peut etre exporte depuis l'ecran de resultats.",
            "GMod Forensic Scanner",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
