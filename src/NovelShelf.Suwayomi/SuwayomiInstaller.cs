using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;

namespace NovelShelf.Suwayomi;

public sealed record InstallProgress(string Step, long Done, long Total);

public sealed class SuwayomiInstaller(SuwayomiPaths paths, HttpClient http)
{
    private string JavaMarker => Path.Combine(paths.Root, "jre.sha256");
    private string ServerMarker => Path.Combine(paths.Root, "server.sha256");

    public bool IsInstalled =>
        File.Exists(paths.JavaExecutable) && File.Exists(paths.ServerJar)
        && ReadMarker(JavaMarker) == SuwayomiRelease.Java.Sha256
        && ReadMarker(ServerMarker) == SuwayomiRelease.Server.Sha256;

    public static long DownloadSize => SuwayomiRelease.Java.ApproximateBytes + SuwayomiRelease.Server.ApproximateBytes;

    public async Task InstallAsync(IProgress<InstallProgress>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(paths.Root);
        var staging = Path.Combine(paths.Root, "staging");
        Directory.CreateDirectory(staging);
        try
        {
            if (!File.Exists(paths.JavaExecutable) || ReadMarker(JavaMarker) != SuwayomiRelease.Java.Sha256)
            {
                var archive = Path.Combine(staging, Path.GetFileName(SuwayomiRelease.Java.Url.AbsolutePath));
                await DownloadAsync(SuwayomiRelease.Java, archive, progress, ct);
                progress?.Report(new InstallProgress("Extraindo Java", 0, 1));
                var target = paths.Java + ".new";
                if (Directory.Exists(target))
                    Directory.Delete(target, recursive: true);
                Directory.CreateDirectory(target);
                await Task.Run(() =>
                {
                    if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        ExtractZip(archive, target);
                    else
                        ExtractTarGz(archive, target);
                }, ct);
                if (Directory.Exists(paths.Java))
                    Directory.Delete(paths.Java, recursive: true);
                Directory.Move(target, paths.Java);
                await File.WriteAllTextAsync(JavaMarker, SuwayomiRelease.Java.Sha256, ct);
            }

            if (!File.Exists(paths.ServerJar) || ReadMarker(ServerMarker) != SuwayomiRelease.Server.Sha256)
            {
                var jar = Path.Combine(staging, "Suwayomi-Server.jar");
                await DownloadAsync(SuwayomiRelease.Server, jar, progress, ct);
                Directory.CreateDirectory(Path.GetDirectoryName(paths.ServerJar)!);
                File.Move(jar, paths.ServerJar, overwrite: true);
                await File.WriteAllTextAsync(ServerMarker, SuwayomiRelease.Server.Sha256, ct);
            }
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    private async Task DownloadAsync(RuntimeArtifact artifact, string destination, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        var step = $"Baixando {artifact.Name}";
        using var response = await http.GetAsync(artifact.Url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? artifact.ApproximateBytes;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var input = await response.Content.ReadAsStreamAsync(ct))
        await using (var output = File.Create(destination))
        {
            var buffer = new byte[1 << 16];
            long done = 0, reported = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (done - reported >= 1 << 20)
                {
                    reported = done;
                    progress?.Report(new InstallProgress(step, done, total));
                }
            }
            progress?.Report(new InstallProgress(step, done, total));
        }

        var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (actual != artifact.Sha256)
        {
            File.Delete(destination);
            throw new InvalidDataException($"{artifact.Name} veio corrompido (SHA-256 {actual}). Tente de novo.");
        }
    }

    internal static void ExtractTarGz(string archive, string destination)
    {
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        using var file = File.OpenRead(archive);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        while (tar.GetNextEntry() is { } entry)
        {
            var target = SafeTarget(root, entry.Name);
            if (target is null)
                continue;
            switch (entry.EntryType)
            {
                case TarEntryType.Directory:
                    Directory.CreateDirectory(target);
                    break;
                case TarEntryType.RegularFile or TarEntryType.V7RegularFile:
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                    if (!OperatingSystem.IsWindows())
                        File.SetUnixFileMode(target, entry.Mode);
                    break;
                case TarEntryType.SymbolicLink:
                    var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(target)!, entry.LinkName));
                    if (!resolved.StartsWith(root, StringComparison.Ordinal))
                        throw new InvalidDataException($"Link fora da pasta do Java: {entry.Name}");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.CreateSymbolicLink(target, entry.LinkName);
                    break;
            }
        }
    }

    internal static void ExtractZip(string archive, string destination)
    {
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(archive);
        foreach (var entry in zip.Entries)
        {
            var target = SafeTarget(root, entry.FullName);
            if (target is null)
                continue;
            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static string? SafeTarget(string root, string entryName)
    {
        var parts = entryName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1)
            return null;
        var target = Path.GetFullPath(Path.Combine(root, Path.Combine(parts[1..])));
        if (!target.StartsWith(root, StringComparison.Ordinal))
            throw new InvalidDataException($"Arquivo fora da pasta do Java: {entryName}");
        return target;
    }

    private static string? ReadMarker(string path) => File.Exists(path) ? File.ReadAllText(path).Trim() : null;
}
