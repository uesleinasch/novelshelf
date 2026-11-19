using System.Text.RegularExpressions;

namespace NovelShelf.Devices;

public sealed partial class KindleLocator(IProcessRunner? runner = null, Func<string, IEnumerable<string>>? listDirectories = null)
{
    private readonly IProcessRunner _runner = runner ?? new ProcessRunner();
    private readonly Func<string, IEnumerable<string>> _listDirectories = listDirectories ?? SafeListDirectories;

    public async Task<IReadOnlyList<KindleDevice>> FindAsync(CancellationToken ct = default)
    {
        var devices = FindMassStorage().Concat(FindGvfsMtp()).ToList();
        if (devices.Count > 0 || !OperatingSystem.IsLinux())
            return devices;

        if (await TryMountMtpAsync(ct))
            devices.AddRange(FindGvfsMtp());
        return devices;
    }

    public IEnumerable<KindleDevice> FindMassStorage()
    {
        foreach (var root in CandidateMountRoots())
        {
            if (LooksLikeKindleRoot(root))
                yield return new KindleDevice(Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)), root, KindleConnection.MassStorage);
        }
    }

    public IEnumerable<KindleDevice> FindGvfsMtp()
    {
        if (!OperatingSystem.IsLinux())
            yield break;

        var gvfs = Path.Combine("/run/user", Environment.GetEnvironmentVariable("UID") ?? GetUid(), "gvfs");
        foreach (var mount in _listDirectories(gvfs))
        {
            var name = Path.GetFileName(mount);
            if (!name.StartsWith("mtp:", StringComparison.Ordinal) || !KindleNameRegex().IsMatch(name))
                continue;

            foreach (var storage in _listDirectories(mount))
            {
                if (_listDirectories(storage).Any(d => Path.GetFileName(d) == "documents"))
                    yield return new KindleDevice(PrettyMtpName(name), storage, KindleConnection.Mtp);
            }
        }
    }

    private async Task<bool> TryMountMtpAsync(CancellationToken ct)
    {
        try
        {
            var (code, output) = await _runner.RunAsync("gio", ["mount", "-li"], ct);
            if (code != 0)
                return false;

            var roots = ActivationRootRegex().Matches(output)
                .Select(m => m.Groups[1].Value)
                .Where(uri => KindleNameRegex().IsMatch(uri))
                .Distinct()
                .ToList();

            var mounted = false;
            foreach (var uri in roots)
            {
                var (mountCode, _) = await _runner.RunAsync("gio", ["mount", uri], ct);
                mounted |= mountCode == 0;
            }
            return mounted;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private IEnumerable<string> CandidateMountRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady && drive.DriveType == DriveType.Removable)
                    yield return drive.RootDirectory.FullName;
            }
            yield break;
        }

        var user = Environment.UserName;
        foreach (var parent in new[] { $"/media/{user}", $"/run/media/{user}", "/media", "/mnt", "/Volumes" })
        {
            foreach (var dir in _listDirectories(parent))
                yield return dir;
        }
    }

    private bool LooksLikeKindleRoot(string root)
    {
        var children = _listDirectories(root).Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return children.Contains("documents") && children.Contains("system");
    }

    private static string PrettyMtpName(string gvfsName)
    {
        var host = gvfsName.Replace("mtp:host=", "", StringComparison.Ordinal);
        var parts = host.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? string.Join(' ', parts.Take(parts.Length - 1)) : host;
    }

    private static string GetUid()
    {
        try
        {
            return File.ReadLines("/proc/self/status")
                .First(l => l.StartsWith("Uid:", StringComparison.Ordinal))
                .Split('\t', StringSplitOptions.RemoveEmptyEntries)[1];
        }
        catch (IOException)
        {
            return "1000";
        }
    }

    private static IEnumerable<string> SafeListDirectories(string path)
    {
        try
        {
            return Directory.Exists(path) ? Directory.GetDirectories(path) : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    [GeneratedRegex("kindle|amazon", RegexOptions.IgnoreCase)]
    private static partial Regex KindleNameRegex();

    [GeneratedRegex(@"activation_root=(mtp://\S+)")]
    private static partial Regex ActivationRootRegex();
}
