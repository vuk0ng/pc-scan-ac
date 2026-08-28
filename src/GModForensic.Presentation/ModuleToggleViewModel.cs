using CommunityToolkit.Mvvm.ComponentModel;
using GModForensic.Abstractions;

namespace GModForensic.Presentation;

/// <summary>Un module dans la liste de selection de l'ecran d'accueil.</summary>
public sealed partial class ModuleToggleViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isEnabled = true;

    public ModuleToggleViewModel(IScanModule module, Capabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(capabilities);

        Id = module.Id;
        DisplayName = module.DisplayName;
        Category = module.Category.ToString();

        // §2 : annoncer AVANT le scan ce qui ne pourra pas etre verifie, et pourquoi.
        UnavailableReason = capabilities.ExplainMissing(module.Requires);
        IsAvailable = UnavailableReason is null;
        _isEnabled = IsAvailable;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public string Category { get; }

    public bool IsAvailable { get; }

    public string? UnavailableReason { get; }

    public string StatusText => IsAvailable ? "disponible" : UnavailableReason!;
}
