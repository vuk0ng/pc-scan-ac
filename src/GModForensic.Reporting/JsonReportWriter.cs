using System.Text.Json;
using System.Text.Json.Serialization;

namespace GModForensic.Reporting;

/// <summary>
/// Export JSON du rapport.
/// <para>
/// Version de l'etape 4 : elle serialise le modele complet et suffit a exploiter un scan par
/// outil. Le rapport HTML autonome — celui que lira le staff — est l'objet de l'etape 8.
/// </para>
/// </summary>
public sealed class JsonReportWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // camelCase : convention attendue par les outils qui consommeront le rapport.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string Write(ForensicReport report, ReportOutputWriter output, string fileName = "rapport.json")
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(output);

        return output.WriteText(fileName, JsonSerializer.Serialize(report, Options));
    }
}
