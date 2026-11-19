namespace NovelShelf.Devices;

public sealed class KindleEjector(IProcessRunner? runner = null)
{
    private readonly IProcessRunner _runner = runner ?? new ProcessRunner();

    public async Task EjectAsync(KindleDevice device, CancellationToken ct = default)
    {
        var (file, args) = Command(device);
        int code;
        string output;
        try
        {
            (code, output) = await _runner.RunAsync(file, args, ct);
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            throw new InvalidOperationException($"Não deu para ejetar: {file} não está disponível ({e.Message}).", e);
        }
        if (code != 0)
        {
            var reason = string.IsNullOrWhiteSpace(output) ? $"código {code}" : output.Trim();
            throw new InvalidOperationException(
                $"Não deu para ejetar o {device.Name}: {reason}. Feche arquivos ou janelas abertos no Kindle e tente de novo.");
        }
    }

    internal static (string File, IReadOnlyList<string> Args) Command(KindleDevice device)
    {
        if (OperatingSystem.IsWindows())
        {
            var drive = Path.GetPathRoot(device.RootPath)!.TrimEnd('\\');
            var script = $"(New-Object -ComObject Shell.Application).Namespace(17).ParseName('{drive}').InvokeVerb('Eject')";
            return ("powershell", ["-NoProfile", "-NonInteractive", "-Command", script]);
        }

        return device.Connection == KindleConnection.Mtp
            ? ("gio", ["mount", "-u", MtpUri(device.RootPath)])
            : ("gio", ["mount", "-e", device.RootPath]);
    }

    internal static string MtpUri(string storagePath)
    {
        var parts = storagePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var mount = parts.FirstOrDefault(p => p.StartsWith("mtp:host=", StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Caminho MTP inesperado: {storagePath}");
        return $"mtp://{mount["mtp:host=".Length..]}/";
    }
}
