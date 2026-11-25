// SPDX-License-Identifier: MIT
using System.Text.Json;
using Microsoft.Win32;

namespace HeadRelay;

public sealed record RegistryValueBackup(string Key, string Name, bool Existed, string? Value, string? InstalledPath = null);

public sealed class GameIntegration(RegistryKey user, string appDirectory)
{
    public const string NPKey = @"SOFTWARE\NaturalPoint\NATURALPOINT\NPClient Location";
    public const string FTKey = @"SOFTWARE\Freetrack\FreetrackClient";
    private const string PathValue = "Path";
    private readonly string bridge = Path.Combine(appDirectory, "Bridge") + Path.DirectorySeparatorChar;
    private readonly string backup = Path.Combine(appDirectory, "Data", "integration-backup.json");

    private static bool SameFolder(string? a, string? b) =>
        a != null && b != null && string.Equals(Path.TrimEndingDirectorySeparator(a),
            Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    private static bool IsOpenTrackModules(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder)) return false;
        var modules = new DirectoryInfo(Path.TrimEndingDirectorySeparator(folder));
        return modules.Name.Equals("modules", StringComparison.OrdinalIgnoreCase)
            && modules.Parent != null
            && File.Exists(Path.Combine(modules.Parent.FullName, "opentrack.exe"))
            && File.Exists(Path.Combine(folder, "NPClient.dll"))
            && File.Exists(Path.Combine(folder, "NPClient64.dll"));
    }

    private string? ReadPath(string key)
    {
        using var section = user.OpenSubKey(key);
        return section?.GetValue(PathValue) as string;
    }

    public string? FindOpenTrack()
    {
        string? registered = ReadPath(NPKey);
        if (IsOpenTrackModules(registered)) return registered;
        foreach (var location in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
        {
            string candidate = Path.Combine(Environment.GetFolderPath(location), "opentrack", "modules");
            if (IsOpenTrackModules(candidate)) return candidate;
        }
        return null;
    }

    public bool Installed() => SameFolder(ReadPath(FTKey), bridge) && IsOpenTrackModules(ReadPath(NPKey));

    private RegistryValueBackup[] ReadBackup()
    {
        var entries = JsonSerializer.Deserialize<RegistryValueBackup[]>(File.ReadAllText(backup));
        if (entries == null || entries.Length != 2 || !entries.Any(e => e?.Key == NPKey)
            || !entries.Any(e => e?.Key == FTKey) || entries.Any(e => e.Name != PathValue || (e.Existed && e.Value == null)))
            throw new InvalidDataException("Die Sicherung der vorherigen Spielanbindung ist unvollständig.");
        return entries;
    }

    private static void RestoreValue(RegistryKey userKey, RegistryValueBackup entry, string? expected)
    {
        using var key = userKey.OpenSubKey(entry.Key, writable: true);
        if (key == null || !SameFolder(key.GetValue(PathValue) as string, expected)) return;
        if (entry.Existed) key.SetValue(PathValue, entry.Value!, RegistryValueKind.String);
        else key.DeleteValue(PathValue, throwOnMissingValue: false);
    }

    public void Install(string modules)
    {
        if (!IsOpenTrackModules(modules))
            throw new DirectoryNotFoundException("OpenTrack mit NPClient.dll und NPClient64.dll wird benötigt. Wähle die installierte opentrack.exe.");
        foreach (string file in new[] { "freetrackclient.dll", "freetrackclient64.dll", "TrackIR.exe" })
            if (!File.Exists(Path.Combine(bridge, file)))
                throw new FileNotFoundException("HeadRelay ist unvollständig. Entpacke die Windows-ZIP erneut.", file);

        string external = Path.TrimEndingDirectorySeparator(Path.GetFullPath(modules)) + Path.DirectorySeparatorChar;
        var previous = File.Exists(backup) ? ReadBackup() : Array.Empty<RegistryValueBackup>();
        var before = new List<RegistryValueBackup>();
        var restore = new List<RegistryValueBackup>();
        foreach (var (key, target) in new[] { (NPKey, external), (FTKey, bridge) })
        {
            string? current = ReadPath(key);
            var snapshot = new RegistryValueBackup(key, PathValue, current != null, current, target);
            before.Add(snapshot);
            var old = previous.SingleOrDefault(e => e.Key == key);
            // A 0.2.0 backup has no InstalledPath: both paths previously pointed at Bridge.
            restore.Add(old != null && SameFolder(current, old.InstalledPath ?? bridge)
                ? old with { InstalledPath = target } : snapshot);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        var pending = backup + ".tmp";
        File.WriteAllText(pending, JsonSerializer.Serialize(restore, new JsonSerializerOptions { WriteIndented = true }));
        int changed = 0;
        try
        {
            foreach (var entry in before)
            {
                using var key = user.CreateSubKey(entry.Key);
                key.SetValue(PathValue, entry.InstalledPath!, RegistryValueKind.String);
                changed++;
            }
            File.Move(pending, backup, overwrite: true);
        }
        catch
        {
            for (int i = changed - 1; i >= 0; i--) RestoreValue(user, before[i], before[i].InstalledPath);
            throw;
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }

    public void Restore()
    {
        if (!File.Exists(backup)) throw new FileNotFoundException("Keine vorherige Einrichtung gesichert.");
        foreach (var entry in ReadBackup()) RestoreValue(user, entry, entry.InstalledPath ?? bridge);
        File.Move(backup, backup + ".restored-" + Guid.NewGuid().ToString("N"));
    }
}
