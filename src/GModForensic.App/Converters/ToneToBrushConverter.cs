using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace GModForensic.App.Converters;

/// <summary>
/// Traduit une cle semantique (« ok », « warn », « crit »...) en pinceau du theme.
/// <para>
/// Les ViewModels n'exposent jamais de couleur : ils exposent un SENS. Cela les garde
/// testables hors WPF et permet de changer le theme sans y toucher.
/// </para>
/// </summary>
public sealed class ToneToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = (value as string) switch
        {
            "ok" => "ToneOk",
            "warn" => "ToneWarn",
            "bad" => "ToneBad",
            "active" => "ToneActive",
            "crit" => "ToneCrit",
            "high" => "ToneHigh",
            "med" => "ToneMed",
            "low" => "ToneLow",
            _ => "ToneMuted",
        };

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
