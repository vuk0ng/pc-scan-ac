using System.Text.RegularExpressions;
using GModForensic.Reporting;
using Xunit;

namespace GModForensic.Tests.Safety;

/// <summary>
/// La RÈGLE ABSOLUE (§1, §27) est d'abord garantie a la compilation par BannedApiAnalyzers.
/// Ces tests protegent le garde-fou lui-meme : ils echouent si quelqu'un retire une interdiction
/// ou multiplie les derogations.
/// <para>
/// La verification au niveau du binaire compile (absence de LoadLibrary, CreateProcess,
/// WriteProcessMemory, CreateRemoteThread dans l'IL) est l'objet de l'etape 9.
/// </para>
/// </summary>
public sealed class AbsoluteRuleTests
{
    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    [Theory]
    [InlineData("T:System.Diagnostics.Process")]
    [InlineData("M:System.Reflection.Assembly.LoadFrom(System.String)")]
    [InlineData("M:System.IO.File.Delete(System.String)")]
    [InlineData("M:System.IO.File.Move(System.String,System.String)")]
    [InlineData("M:System.IO.File.Copy(System.String,System.String)")]
    [InlineData("M:Microsoft.Win32.RegistryKey.SetValue(System.String,System.Object)")]
    [InlineData("M:Microsoft.Win32.RegistryKey.DeleteValue(System.String)")]
    [InlineData("T:System.Net.Http.HttpClient")]
    public void L_interdiction_reste_declaree(string symbol)
    {
        var banned = File.ReadAllText(Path.Combine(RepositoryRoot, "BannedSymbols.txt"));

        Assert.Contains(symbol + ";", banned, StringComparison.Ordinal);
    }

    [Fact]
    public void Le_garde_fou_ne_peut_pas_etre_reduit_a_un_simple_avertissement()
    {
        var editorConfig = File.ReadAllText(Path.Combine(RepositoryRoot, ".editorconfig"));

        Assert.Contains("dotnet_diagnostic.RS0030.severity = error", editorConfig, StringComparison.Ordinal);
    }

    [Fact]
    public void Les_seules_derogations_du_produit_sont_concentrees_dans_ReportOutputWriter()
    {
        var offenders = Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("#pragma warning disable RS0030", StringComparison.Ordinal))
            .Select(path => Path.GetFileName(path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Une derogation ailleurs qu'ici doit provoquer une revue explicite, pas passer inapercue.
        Assert.Equal(["ReportOutputWriter.cs"], offenders);
    }

    [Fact]
    public void Aucune_source_du_produit_n_appelle_une_api_d_execution_ou_d_injection()
    {
        // Filet complementaire a l'analyseur : il couvre aussi les declarations DllImport,
        // qui ne passent pas par les symboles bannis.
        var forbidden = new Regex(
            @"\b(CreateRemoteThread|WriteProcessMemory|VirtualAllocEx|VirtualProtectEx|LoadLibrary[AW]?|CreateProcess[AW]?|ShellExecute[AW]?|TerminateProcess|AdjustTokenPrivileges)\b",
            RegexOptions.Compiled);

        var offenders = new List<string>();

        foreach (var path in Directory.EnumerateFiles(
                     Path.Combine(RepositoryRoot, "src"), "*.cs", SearchOption.AllDirectories))
        {
            // Les fichiers generes par CsWin32 sont exclus : seul NativeMethods.txt, revu a la main,
            // decide de ce qui est genere (et il est verifie par le test suivant).
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in forbidden.Matches(File.ReadAllText(path)))
            {
                offenders.Add($"{Path.GetFileName(path)} : {match.Value}");
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void La_liste_des_api_natives_generees_ne_contient_aucune_api_d_ecriture()
    {
        var manifest = File.ReadAllText(
            Path.Combine(RepositoryRoot, "src", "GModForensic.Native", "NativeMethods.txt"));

        string[] forbidden =
        [
            "WriteProcessMemory", "CreateRemoteThread", "VirtualAllocEx", "VirtualProtectEx",
            "LoadLibrary", "CreateProcess", "TerminateProcess", "RegSetValue", "RegDeleteValue",
            "SetFileAttributes", "DeleteFile", "MoveFile", "AdjustTokenPrivileges",
        ];

        foreach (var api in forbidden)
        {
            Assert.DoesNotContain(api, manifest, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Le_redacteur_de_rapport_refuse_de_sortir_de_son_dossier()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gmodforensic-tests", Guid.NewGuid().ToString("n"));

        try
        {
            var writer = new ReportOutputWriter(directory);

            // Un nom de fichier issu du systeme analyse est une donnee hostile (limite L11).
            Assert.Throws<ArgumentException>(() =>
                writer.WriteText(Path.Combine("..", "..", "evade.txt"), "contenu"));

            var written = writer.WriteText("rapport.json", "{}");
            Assert.StartsWith(writer.Root, written, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Le_lecteur_de_rapport_n_injecte_jamais_de_balisage_issu_des_donnees()
    {
        var reader = File.ReadAllText(Path.Combine(RepositoryRoot, "tools", "report-reader.html"));

        // Les noms de fichiers d'un rapport viennent de la machine analysee : ce sont des
        // donnees hostiles (limite L11). Tout doit passer par textContent / createElement.
        Assert.DoesNotContain("innerHTML", reader, StringComparison.Ordinal);
        Assert.DoesNotContain("outerHTML", reader, StringComparison.Ordinal);
        Assert.DoesNotContain("insertAdjacentHTML", reader, StringComparison.Ordinal);
        Assert.DoesNotContain("document.write", reader, StringComparison.Ordinal);
        Assert.DoesNotContain("eval(", reader, StringComparison.Ordinal);

        // Traitement entierement local : aucune ressource ni requete externe.
        Assert.DoesNotContain("http://", reader, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", reader, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fetch(", reader, StringComparison.Ordinal);
        Assert.DoesNotContain("XMLHttpRequest", reader, StringComparison.Ordinal);
    }

    [Fact]
    public void La_globalisation_invariante_reste_desactivee()
    {
        // Regression. Avec InvariantGlobalization, seule la culture invariante existe :
        // le moteur de texte de WPF leve des son premier calcul de mise en page
        // (« Cannot find non-neutral culture related to en-us ») et la fenetre ne
        // s'affiche jamais. Aucun test d'interface ne peut l'attraper — les tests ne
        // demarrent pas WPF — d'ou cette verification sur la configuration elle-meme.
        var props = File.ReadAllText(Path.Combine(RepositoryRoot, "Directory.Build.props"));

        Assert.DoesNotContain("<InvariantGlobalization>true</InvariantGlobalization>", props,
            StringComparison.OrdinalIgnoreCase);

        // Et sur l'artefact reellement produit, quand il a ete compile.
        var appOutput = Path.Combine(RepositoryRoot, "src", "GModForensic.App", "bin");

        if (!Directory.Exists(appOutput))
        {
            return;
        }

        foreach (var config in Directory.EnumerateFiles(
                     appOutput, "GModForensicScanner.runtimeconfig.json", SearchOption.AllDirectories))
        {
            Assert.DoesNotContain("\"System.Globalization.Invariant\": true",
                File.ReadAllText(config), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Aucun_secret_de_credential_n_est_jamais_lu()
    {
        // §17 — INTERDICTION ABSOLUE de lire, afficher ou exporter un mot de passe.
        // La structure CREDENTIALW expose CredentialBlob : aucun code du produit ne doit
        // le nommer, ni appeler CredRead, ni toucher a la DPAPI.
        string[] forbidden =
        [
            "CredentialBlob", "CredRead", "CryptUnprotectData", "ProtectedData.Unprotect",
            "Microsoft\\Credentials", "Login Data", "Local Storage", "leveldb", "Cookies",
        ];

        var offenders = new List<string>();

        foreach (var path in Directory.EnumerateFiles(
                     Path.Combine(RepositoryRoot, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(path);

            foreach (var token in forbidden)
            {
                // Une mention en commentaire explicatif est autorisee ; un usage ne l'est pas.
                foreach (var line in text.Split('\n'))
                {
                    var trimmed = line.TrimStart();

                    if (trimmed.StartsWith("//", StringComparison.Ordinal)
                        || trimmed.StartsWith("///", StringComparison.Ordinal)
                        || trimmed.StartsWith("*", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (line.Contains(token, StringComparison.OrdinalIgnoreCase))
                    {
                        offenders.Add($"{Path.GetFileName(path)} : {token}");
                    }
                }
            }
        }

        Assert.Empty(offenders);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BannedSymbols.txt")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Racine du depot introuvable depuis " + AppContext.BaseDirectory);
    }
}
