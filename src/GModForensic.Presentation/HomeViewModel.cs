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
    private Capabilities _capabilities = Capabilities.None;

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

    [ObservableProperty]
    private bool _isMeasuring = true;

    public HomeViewModel(IScanSession session, Func<Task>? startRequested = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        StartRequested = startRequested;

        var defaults = new ScanConfiguration();
        _fileSystemRoots = string.Join(Environment.NewLine, defaults.FileSystemRoots);

        Modules = [];
        Unavailable = [];

        // Les capacites arrivent apres l'affichage de la fenetre : voir ApplyCapabilities.
        if (_session.Capabilities is { } already)
        {
            ApplyCapabilities(already);
        }
    }

    /// <summary>
    /// Renseigne l'ecran une fois les privileges mesures.
    /// <para>
    /// La mesure sonde le jeton, les volumes et le dossier Prefetch ; elle est faite hors du
    /// fil d'interface pour que la fenetre s'affiche immediatement, quoi qu'il arrive.
    /// </para>
    /// </summary>
    public void ApplyCapabilities(Capabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        _capabilities = capabilities;

        Modules.Clear();
        Unavailable.Clear();

        foreach (var module in _session.Modules)
        {
            var toggle = new ModuleToggleViewModel(module, capabilities);
            Modules.Add(toggle);

            if (!toggle.IsAvailable)
            {
                Unavailable.Add(toggle);
            }
        }

        IsMeasuring = false;

        OnPropertyChanged(nameof(Capabilities));
        OnPropertyChanged(nameof(CapabilitySummary));
        OnPropertyChanged(nameof(CapabilityNotes));
        OnPropertyChanged(nameof(HasUnavailableModules));
        NotifyCanStart();
    }

    /// <summary>Appele quand le staff lance le scan. Fourni par le shell.</summary>
    public Func<Task>? StartRequested { get; set; }

    public ObservableCollection<ModuleToggleViewModel> Modules { get; }

    /// <summary>Modules qui seront ignores, avec leur motif — affiches sans avoir a derouler la liste.</summary>
    public ObservableCollection<ModuleToggleViewModel> Unavailable { get; }

    public Capabilities Capabilities => _capabilities;

    public bool HasUnavailableModules => Unavailable.Count > 0;

    public string CapabilitySummary => IsMeasuring
        ? "Mesure des privileges en cours..."
        : $"Administrateur : {YesNo(Capabilities.IsElevated)}"
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
        !IsMeasuring
        && ConsentGiven
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

    partial void OnIsMeasuringChanged(bool value) => NotifyCanStart();

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
