using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GModForensic.Abstractions;
using GModForensic.Presentation.Services;

namespace GModForensic.Presentation;

/// <summary>
/// Ecran d'accueil : capacites reellement obtenues, tracabilite du controle, consentement,
/// profil et selection des modules.
/// </summary>
public sealed partial class HomeViewModel : ObservableObject
{
    private readonly IScanSession _session;

    [ObservableProperty]
    private string _operatorName = string.Empty;

    [ObservableProperty]
    private string _subjectIdentifier = string.Empty;

    [ObservableProperty]
    private string _reference = string.Empty;

    [ObservableProperty]
    private bool _consentGiven;

    [ObservableProperty]
    private ScanProfile _profile = ScanProfile.Standard;

    [ObservableProperty]
    private string _fileSystemRoots;

    public HomeViewModel(IScanSession session, Func<Task>? startRequested = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        StartRequested = startRequested;

        var defaults = new ScanConfiguration();
        _fileSystemRoots = string.Join(Environment.NewLine, defaults.FileSystemRoots);

        Modules = new ObservableCollection<ModuleToggleViewModel>(
            _session.Modules.Select(m => new ModuleToggleViewModel(m, _session.Capabilities)));

        Unavailable = new ObservableCollection<ModuleToggleViewModel>(
            Modules.Where(m => !m.IsAvailable));
    }

    /// <summary>Appele quand le staff lance le scan. Fourni par le shell.</summary>
    public Func<Task>? StartRequested { get; set; }

    public ObservableCollection<ModuleToggleViewModel> Modules { get; }

    /// <summary>Modules qui seront ignores, avec leur motif — affiches sans avoir a derouler la liste.</summary>
    public ObservableCollection<ModuleToggleViewModel> Unavailable { get; }

    public Capabilities Capabilities => _session.Capabilities;

    public bool HasUnavailableModules => Unavailable.Count > 0;

    public string CapabilitySummary =>
        $"Administrateur : {YesNo(Capabilities.IsElevated)}"
        + $"   ·   SeDebugPrivilege : {YesNo(Capabilities.HasDebugPrivilege)}"
        + $"   ·   SeSecurityPrivilege : {YesNo(Capabilities.HasSecurityPrivilege)}";

    public IReadOnlyList<string> CapabilityNotes => Capabilities.Notes;

    public IReadOnlyList<ScanProfile> Profiles { get; } =
        [ScanProfile.Quick, ScanProfile.Standard, ScanProfile.Deep];

    public string ProfileDescription => Profile switch
    {
        ScanProfile.Quick => "Registre, Prefetch et processus. Retour en moins d'une minute.",
        ScanProfile.Deep => "Ajoute l'analyse memoire et le journal USN complet. 5 a 10 minutes.",
        _ => "Couverture complete hors analyse memoire. 2 a 4 minutes.",
    };

    /// <summary>
    /// Le consentement et la tracabilite sont des prerequis, pas des options : l'outil lit des
    /// artefacts detailles sur une machine personnelle.
    /// </summary>
    public bool CanStart =>
        ConsentGiven
        && !string.IsNullOrWhiteSpace(OperatorName)
        && !string.IsNullOrWhiteSpace(SubjectIdentifier)
        && Modules.Any(m => m.IsEnabled && m.IsAvailable);

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (StartRequested is not null)
        {
            await StartRequested().ConfigureAwait(false);
        }
    }

    public ScanConfiguration BuildConfiguration()
    {
        var disabled = Modules
            .Where(m => !m.IsEnabled || !m.IsAvailable)
            .Select(m => m.Id);

        var roots = FileSystemRoots
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var defaults = new ScanConfiguration();

        return defaults with
        {
            Profile = Profile,
            OperatorName = OperatorName.Trim(),
            SubjectIdentifier = SubjectIdentifier.Trim(),
            ConsentGiven = ConsentGiven,
            DisabledModuleIds = new HashSet<string>(disabled, StringComparer.OrdinalIgnoreCase),
            FileSystemRoots = roots.Length > 0 ? roots : defaults.FileSystemRoots,
        };
    }

    partial void OnConsentGivenChanged(bool value) => NotifyCanStart();

    partial void OnOperatorNameChanged(string value) => NotifyCanStart();

    partial void OnSubjectIdentifierChanged(string value) => NotifyCanStart();

    partial void OnProfileChanged(ScanProfile value) => OnPropertyChanged(nameof(ProfileDescription));

    private void NotifyCanStart()
    {
        OnPropertyChanged(nameof(CanStart));
        StartCommand.NotifyCanExecuteChanged();
    }

    private static string YesNo(bool value) => value ? "OUI" : "NON";
}
