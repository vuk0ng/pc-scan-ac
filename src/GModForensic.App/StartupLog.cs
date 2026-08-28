using System.IO;
using System.Globalization;
using GModForensic.Reporting;

namespace GModForensic.App;

/// <summary>
/// Journal de demarrage, ecrit dans
/// <c>%LOCALAPPDATA%\GModForensicScanner\startup.log</c>.
/// <para>
/// Un programme eleve qui echoue avant d'afficher sa fenetre ne laisse aucune trace visible :
/// ce journal est le seul moyen de savoir ou il s'est arrete. L'ecriture passe par
/// <see cref="ReportOutputWriter"/>, seul composant autorise a ecrire (RÈGLE ABSOLUE).
/// </para>
/// </summary>
internal static class StartupLog
{
    private static readonly ReportOutputWriter? Writer = TryCreateWriter();

    public static string? Path { get; private set; }

    public static void Write(string message)
    {
        try
        {
            var line = string.Create(CultureInfo.InvariantCulture,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}");

            Path = Writer?.AppendLine("startup.log", line);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Un journal indisponible ne doit jamais empecher le programme de demarrer.
        }
    }

    public static void Write(string message, Exception exception) =>
        Write($"{message} — {exception.GetType().Name} : {exception.Message}"
              + Environment.NewLine + exception.StackTrace);

    private static ReportOutputWriter? TryCreateWriter()
    {
        try
        {
            return new ReportOutputWriter(System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GModForensicScanner"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
