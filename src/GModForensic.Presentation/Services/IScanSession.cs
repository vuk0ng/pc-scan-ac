using GModForensic.Abstractions;
using GModForensic.Engine;

namespace GModForensic.Presentation.Services;

/// <summary>
/// Ce dont l'interface a besoin pour lancer un scan, sans jamais dependre des modules
/// eux-memes — qui ne compilent que sous Windows.
/// </summary>
public interface IScanSession
{
    /// <summary>Capacites reellement obtenues, mesurees au demarrage du programme.</summary>
    Capabilities Capabilities { get; }

    /// <summary>Modules disponibles, dans l'ordre ou ils seront executes.</summary>
    IReadOnlyList<IScanModule> Modules { get; }

    Task<ScanRunResult> RunAsync(
        ScanConfiguration configuration,
        IProgress<ScanProgressSnapshot> progress,
        CancellationToken cancellationToken);
}

/// <summary>Fournit les modules. Implemente par l'application Windows.</summary>
public interface IScanModuleProvider
{
    IReadOnlyList<IScanModule> CreateModules();
}

/// <summary>Fournit les capacites. Implemente par l'application Windows (CapabilityProbe).</summary>
public interface ICapabilityProvider
{
    Capabilities Measure();
}
