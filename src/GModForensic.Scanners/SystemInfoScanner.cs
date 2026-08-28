using System.Globalization;
using GModForensic.Abstractions;
using GModForensic.Abstractions.Model;
using Microsoft.Win32;

namespace GModForensic.Scanners;

/// <summary>
/// M01 — contexte du rapport.
/// <para>
/// Ce module ne cherche pas de cheat : il etablit le contexte sans lequel les autres
/// observations ne veulent rien dire. La date d'installation de Windows, notamment, est ce
/// qui distingue une machine nettoyee d'une machine simplement reinstallee (M18).
/// </para>
/// </summary>
public sealed class SystemInfoScanner : IScanModule
{
    public string Id => "system";

    public string DisplayName => "Informations systeme";

    public ScanCategory Category => ScanCategory.System;

    public RequiredCapabilities Requires => RequiredCapabilities.None;

    public int Weight => 2;

    public Task<ModuleResult> RunAsync(ScanContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var run = new ModuleRun(Id, context);
        var now = context.Clock.GetUtcNow();

        run.Progress(0.1, "Version de Windows");
        ReadWindowsVersion(run, now, cancellationToken);

        run.Progress(0.5, "Volumes");
        ReadVolumes(run, now);

        run.Progress(0.8, "Demarrage securise");
        ReadSecureBoot(run, now);

        run.Add(Fact(run, now, "Uptime", new()
        {
            ["uptime"] = TimeSpan.FromMilliseconds(Environment.TickCount64).ToString(@"d\j\ hh\:mm", CultureInfo.InvariantCulture),
            ["machine"] = Environment.MachineName,
            ["user"] = Environment.UserName,
            ["timeZone"] = TimeZoneInfo.Local.Id,
            ["localTime"] = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
        }, "GetTickCount64"));

        run.Progress(1, "Termine");
        return Task.FromResult(run.ToResult());
    }

    private void ReadWindowsVersion(ModuleRun run, DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var key = run.OpenKey(RegistryHive.LocalMachine,
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");

        if (key is null)
        {
            return;
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["productName"] = key.GetValue("ProductName")?.ToString() ?? "?",
            ["displayVersion"] = key.GetValue("DisplayVersion")?.ToString() ?? "?",
            ["build"] = $"{key.GetValue("CurrentBuild")}.{key.GetValue("UBR")}",
        };

        // InstallDate est un temps Unix en secondes. C'est la donnee de contexte la plus
        // importante du rapport : une machine installee il y a trois jours explique a elle
        // seule un Prefetch vide, un BAM vide et un journal USN court.
        if (key.GetValue("InstallDate") is int installed and > 0)
        {
            fields["installedUtc"] = DateTimeOffset.FromUnixTimeSeconds(installed)
                .ToString("O", CultureInfo.InvariantCulture);
            fields["installedDaysAgo"] = ((int)(now - DateTimeOffset.FromUnixTimeSeconds(installed)).TotalDays)
                .ToString(CultureInfo.InvariantCulture);
        }

        run.Add(Fact(run, now, "WindowsVersion", fields,
            @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion"));
    }

    private void ReadVolumes(ModuleRun run, DateTimeOffset now)
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                // DriveType d'abord : lire IsReady sur un lecteur reseau deconnecte bloque.
                if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || !drive.IsReady)
                {
                    continue;
                }

                run.Add(Fact(run, now, "Volume", new()
                {
                    ["name"] = drive.Name,
                    ["format"] = drive.DriveFormat,
                    ["type"] = drive.DriveType.ToString(),
                    ["sizeGb"] = (drive.TotalSize / 1024d / 1024 / 1024).ToString("0.0", CultureInfo.InvariantCulture),
                    ["freeGb"] = (drive.AvailableFreeSpace / 1024d / 1024 / 1024).ToString("0.0", CultureInfo.InvariantCulture),
                }, "DriveInfo"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                run.Note(Diagnostic.AccessDenied(drive.Name));
            }
        }
    }

    private void ReadSecureBoot(ModuleRun run, DateTimeOffset now)
    {
        using var key = run.OpenKey(RegistryHive.LocalMachine,
            @"SYSTEM\CurrentControlSet\Control\SecureBoot\State");

        if (key is null)
        {
            return;
        }

        var enabled = key.GetValue("UEFISecureBootEnabled") as int?;

        run.Add(Fact(run, now, "SecureBoot", new()
        {
            ["enabled"] = enabled switch { 1 => "oui", 0 => "non", _ => "inconnu" },
        }, @"HKLM\SYSTEM\CurrentControlSet\Control\SecureBoot\State"));
    }

    private Observation Fact(
        ModuleRun run,
        DateTimeOffset now,
        string kind,
        Dictionary<string, string> fields,
        string source) => new()
        {
            ModuleId = Id,
            Kind = ObservationKind.SystemFact,
            Timestamp = now,
            Fields = fields,
            Source = source,
            Evidence = Evidence.FromText(
                "SystemFact",
                source,
                string.Join(" · ", fields.Select(f => $"{f.Key}={f.Value}")),
                $"Verifiable dans : {source}"),
        };
}
