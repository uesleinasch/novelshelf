namespace NovelShelf.Devices;

public sealed class KindleSender(IProcessRunner? runner = null)
{
    public const string Folder = "NovelShelf";

    private readonly IProcessRunner _runner = runner ?? new ProcessRunner();

    public async Task<SendResult> SendAsync(KindleDevice device, SendRequest request, CancellationToken ct = default)
    {
        var targetDir = Path.Combine(device.DocumentsPath, Folder);
        var target = Path.Combine(targetDir, request.TargetFileName);

        if (device.Connection == KindleConnection.Mtp && OperatingSystem.IsLinux())
            await CopyWithGioAsync(request.LocalFile, targetDir, target, ct);
        else
            CopyWithFileSystem(request.LocalFile, targetDir, target);

        var thumbnailWritten = false;
        if (request.ThumbnailJpeg is { } thumb && request.ThumbnailFileName is { } thumbName && Directory.Exists(device.ThumbnailsPath))
        {
            try
            {
                var thumbPath = Path.Combine(device.ThumbnailsPath, thumbName);
                if (device.Connection == KindleConnection.Mtp && OperatingSystem.IsLinux())
                {
                    var temp = Path.Combine(Path.GetTempPath(), thumbName);
                    await File.WriteAllBytesAsync(temp, thumb, ct);
                    await CopyWithGioAsync(temp, device.ThumbnailsPath, thumbPath, ct);
                    File.Delete(temp);
                }
                else
                {
                    await File.WriteAllBytesAsync(thumbPath, thumb, ct);
                }
                thumbnailWritten = true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
            }
        }

        return new SendResult(target, thumbnailWritten);
    }

    public async Task<IReadOnlyList<string>> RemoveAsync(KindleDevice device, Func<string, bool> shouldRemove, CancellationToken ct = default)
    {
        var targetDir = Path.Combine(device.DocumentsPath, Folder);
        if (!Directory.Exists(targetDir))
            return [];
        var removed = new List<string>();
        foreach (var file in Directory.EnumerateFiles(targetDir).Where(f => shouldRemove(Path.GetFileName(f))).ToList())
        {
            if (device.Connection == KindleConnection.Mtp && OperatingSystem.IsLinux())
                await RunGioAsync(["remove", file], ct);
            else
                File.Delete(file);
            removed.Add(Path.GetFileName(file));
        }
        return removed;
    }

    private static void CopyWithFileSystem(string source, string targetDir, string target)
    {
        Directory.CreateDirectory(targetDir);
        var temp = target + ".part";
        File.Copy(source, temp, overwrite: true);
        File.Move(temp, target, overwrite: true);
    }

    // gvfs-mtp can't truncate files in place, so an existing book is removed before the copy.
    private async Task CopyWithGioAsync(string source, string targetDir, string target, CancellationToken ct)
    {
        if (!Directory.Exists(targetDir))
            await RunGioAsync(["mkdir", "-p", targetDir], ct);
        if (File.Exists(target))
            await RunGioAsync(["remove", target], ct);
        await RunGioAsync(["copy", source, target], ct);
    }

    private async Task RunGioAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        var (code, output) = await _runner.RunAsync("gio", args, ct);
        if (code != 0)
            throw new InvalidOperationException($"gio {string.Join(' ', args)} falhou: {output.Trim()}");
    }
}
