using GModForensic.Abstractions;
using GModForensic.Abstractions.Logging;
using GModForensic.Abstractions.Model;
using Microsoft.Win32;

namespace GModForensic.Scanners;

/// <summary>
/// Etat d'une execution de module : observations, diagnostics, progression et acces registre.
/// <para>
/// Instancie a chaque <c>RunAsync</c> — un module ne conserve jamais d'etat entre deux scans.
/// Centralise aussi la regle du §25 : un acces refuse devient un diagnostic, jamais une
/// exception qui remonte.
/// </para>
/// </summary>
internal sealed class ModuleRun
{
    private readonly ScanContext _context;
    private readonly List<Observation> _observations = [];
    private readonly List<Diagnostic> _diagnostics = [];

    public ModuleRun(string moduleId, ScanContext context)
    {
        ModuleId = moduleId;
        _context = context;
    }

    public string ModuleId { get; }

    public int ItemsExamined { get; private set; }

    public void Add(Observation observation)
    {
        _observations.Add(observation);
        ItemsExamined++;
    }

    public void Counted() => ItemsExamined++;

    public void Note(Diagnostic diagnostic) => _diagnostics.Add(diagnostic);

    public void Progress(double fraction, string step) =>
        _context.Progress.Report(new ModuleProgress(ModuleId, fraction, step, ItemsExamined));

    /// <summary>
    /// Ouvre une cle en LECTURE SEULE. Une cle absente est normale, un acces refuse est
    /// signale — dans les deux cas le module continue.
    /// </summary>
    public RegistryKey? OpenKey(RegistryHive hive, string path, RegistryView view = RegistryView.Registry64)
    {
        var display = $"{Describe(hive)}\\{path}";

        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            var key = root.OpenSubKey(path);

            if (key is null)
            {
                Note(Diagnostic.NotFound(display));
                return null;
            }

            _context.Logger.RecordAccess(ModuleId, "RegistryKey", display);
            return key;
        }
        catch (System.Security.SecurityException)
        {
            Note(Diagnostic.AccessDenied(display));
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            Note(Diagnostic.AccessDenied(display));
            return null;
        }
        catch (IOException ex)
        {
            Note(Diagnostic.Error(display, ex.Message));
            return null;
        }
    }

    public ModuleResult ToResult(string? statusReason = null)
    {
        var failed = _diagnostics.Any(d => d.Level == DiagnosticLevel.Error);
        var degraded = _diagnostics.Any(d => d.Level == DiagnosticLevel.Warning);

        var status = failed
            ? ModuleStatus.Partial
            : degraded ? ModuleStatus.Partial : ModuleStatus.Success;

        return new ModuleResult
        {
            ModuleId = ModuleId,
            Status = status,
            Observations = _observations,
            Diagnostics = _diagnostics,
            ItemsExamined = ItemsExamined,
            StatusReason = statusReason ?? (degraded || failed
                ? $"{_diagnostics.Count(d => d.Level != DiagnosticLevel.Info)} acces impossibles"
                : null),
        };
    }

    private static string Describe(RegistryHive hive) => hive switch
    {
        RegistryHive.LocalMachine => "HKLM",
        RegistryHive.CurrentUser => "HKCU",
        RegistryHive.ClassesRoot => "HKCR",
        RegistryHive.Users => "HKU",
        _ => hive.ToString(),
    };
}
