using System.Text;

namespace GModForensic.Reporting;

/// <summary>
/// SEUL composant du produit autorise a ecrire sur disque, et uniquement sous le dossier de
/// sortie choisi par le staff.
/// <para>
/// Toutes les API d'ecriture de <c>System.IO.File</c> sont interdites par BannedSymbols.txt.
/// Les quelques suppressions <c>RS0030</c> ci-dessous sont les seules du produit : elles sont
/// concentrees ici, commentees, et donc auditables d'un coup d'oeil.
/// </para>
/// </summary>
public sealed class ReportOutputWriter
{
    private readonly string _root;

    /// <param name="outputDirectory">Dossier de sortie choisi par le staff. Cree s'il n'existe pas.</param>
    public ReportOutputWriter(string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(outputDirectory);

        _root = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;

    /// <summary>
    /// Ecrit un fichier du rapport. <paramref name="fileName"/> ne peut pas s'echapper du dossier
    /// de sortie : toute tentative de traversee est refusee, car un nom de fichier issu du systeme
    /// analyse est une donnee hostile (limite L11 de docs/01).
    /// </summary>
    public string WriteText(string fileName, string content)
    {
        var target = ResolveInsideRoot(fileName);

        // RS0030 justifie : ReportOutputWriter est le point d'ecriture unique du produit,
        // et la cible est contrainte au dossier de rapport par ResolveInsideRoot.
#pragma warning disable RS0030
        File.WriteAllText(target, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
#pragma warning restore RS0030

        return target;
    }

    /// <summary>Ajoute une ligne au journal d'execution, en continu pendant le scan.</summary>
    public string AppendLine(string fileName, string line)
    {
        var target = ResolveInsideRoot(fileName);

        // RS0030 justifie : meme raison que WriteText.
#pragma warning disable RS0030
        File.AppendAllText(target, line + Environment.NewLine, Encoding.UTF8);
#pragma warning restore RS0030

        return target;
    }

    private string ResolveInsideRoot(string fileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        var target = Path.GetFullPath(Path.Combine(_root, fileName));

        if (!target.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(target, _root, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Le nom « {fileName} » sortirait du dossier de rapport.", nameof(fileName));
        }

        return target;
    }
}
