using GModForensic.Abstractions;
using GModForensic.Native.Security;
using GModForensic.Presentation.Services;
using GModForensic.Scanners;

namespace GModForensic.App.Services;

/// <summary>Mesure les privileges reellement obtenus au demarrage (§2).</summary>
public sealed class WindowsCapabilityProvider : ICapabilityProvider
{
    public Capabilities Measure() => CapabilityProbe.Measure();
}

/// <summary>
/// Fournit les modules de DEMONSTRATION de l'etape 4.
/// Remplace par le catalogue reel a l'etape 5, sans aucun changement dans l'interface.
/// </summary>
public sealed class DemoModuleProvider : IScanModuleProvider
{
    public IReadOnlyList<IScanModule> CreateModules() => ModuleCatalog.CreateDemoModules();
}
