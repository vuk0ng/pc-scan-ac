using System.IO;
using Microsoft.Win32;

namespace GModForensic.App.Services;

/// <summary>Selecteur de dossier de sortie. Aucune lecture, aucune ecriture : un simple choix.</summary>
public static class FolderPicker
{
    public static string? Pick(string initialDirectory)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Dossier de sortie du rapport",
            Multiselect = false,
        };

        try
        {
            if (Directory.Exists(initialDirectory))
            {
                dialog.InitialDirectory = initialDirectory;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Un dossier initial inaccessible n'est pas une erreur : la boite s'ouvre ailleurs.
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
