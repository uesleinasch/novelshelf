using System.Diagnostics;

namespace NovelShelf.Devices;

public interface IProcessRunner
{
    Task<(int ExitCode, string Output)> RunAsync(string file, IReadOnlyList<string> args, CancellationToken ct = default);
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<(int ExitCode, string Output)> RunAsync(string file, IReadOnlyList<string> args, CancellationToken ct = default)
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        info.Environment["LC_ALL"] = "C";

        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Não foi possível iniciar {file}.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return (process.ExitCode, await stdout + await stderr);
    }
}
