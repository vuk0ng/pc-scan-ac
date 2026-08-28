using CommunityToolkit.Mvvm.ComponentModel;
using GModForensic.Abstractions.Model;
using GModForensic.Engine;

namespace GModForensic.Presentation;

/// <summary>Ligne d'etat d'un module pendant le scan.</summary>
public sealed partial class ModuleRowViewModel : ObservableObject
{
    [ObservableProperty]
    private ModuleStatus _status = ModuleStatus.NotStarted;

    [ObservableProperty]
    private double _fraction;

    [ObservableProperty]
    private int _itemsExamined;

    [ObservableProperty]
    private string? _statusReason;

    public ModuleRowViewModel(string moduleId, string displayName)
    {
        ModuleId = moduleId;
        DisplayName = displayName;
    }

    public string ModuleId { get; }

    public string DisplayName { get; }

    /// <summary>Symboles du §25 : ✓ reussi, ⚠ partiel, ✕ impossible, ○ ignore, ⊘ annule.</summary>
    public string Symbol => Status switch
    {
        ModuleStatus.Success => "✓",
        ModuleStatus.Partial => "⚠",
        ModuleStatus.Failed => "✕",
        ModuleStatus.Skipped => "○",
        ModuleStatus.Cancelled => "⊘",
        ModuleStatus.Running => "⟳",
        _ => "·",
    };

    /// <summary>
    /// Cle semantique de couleur. La couleur ne porte jamais seule l'information :
    /// le symbole et le texte restent lisibles sans elle.
    /// </summary>
    public string Tone => Status switch
    {
        ModuleStatus.Success => "ok",
        ModuleStatus.Partial => "warn",
        ModuleStatus.Failed => "bad",
        ModuleStatus.Running => "active",
        _ => "muted",
    };

    public string Detail => StatusReason is not null
        ? $"{ItemsExamined} elements — {StatusReason}"
        : $"{ItemsExamined} elements";

    public void Apply(ModuleSnapshot snapshot)
    {
        Status = snapshot.Status;
        Fraction = snapshot.Fraction;
        ItemsExamined = snapshot.ItemsExamined;
        StatusReason = snapshot.StatusReason;
    }

    partial void OnStatusChanged(ModuleStatus value)
    {
        OnPropertyChanged(nameof(Symbol));
        OnPropertyChanged(nameof(Tone));
    }

    partial void OnItemsExaminedChanged(int value) => OnPropertyChanged(nameof(Detail));

    partial void OnStatusReasonChanged(string? value) => OnPropertyChanged(nameof(Detail));
}
