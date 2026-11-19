using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NovelShelf.Suwayomi;

public enum SuwayomiState
{
    NotInstalled,
    Stopped,
    Starting,
    Running,
    Failed,
}

public sealed class SuwayomiServer(SuwayomiPaths paths, SuwayomiInstaller installer, HttpClient http) : IAsyncDisposable
{
    private const string ConfigPrefix = "-Dsuwayomi.tachidesk.config.server.";

    private const string Watchdog = """
        "$@" </dev/null &
        child=$!
        echo "$child" > "$NOVELSHELF_PID_FILE"
        trap 'kill -TERM "$child" 2>/dev/null' TERM INT HUP
        read -r _
        kill -TERM "$child" 2>/dev/null
        wait "$child"
        """;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private IDisposable? _job;
    private StreamWriter? _log;

    public SuwayomiState State { get; private set; } = installer.IsInstalled ? SuwayomiState.Stopped : SuwayomiState.NotInstalled;
    public string? Error { get; private set; }
    public Uri? BaseUrl { get; private set; }

    public event EventHandler? StateChanged;

    public async Task<Uri> StartAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (State == SuwayomiState.Running && BaseUrl is not null && _process is { HasExited: false })
                return BaseUrl;
            if (!installer.IsInstalled)
            {
                SetState(SuwayomiState.NotInstalled, null);
                throw new InvalidOperationException("O suporte a mangás ainda não foi instalado.");
            }

            try
            {
                return await LaunchAsync(ct);
            }
            catch (OperationCanceledException)
            {
                await StopCoreAsync();
                SetState(SuwayomiState.Stopped, null);
                throw;
            }
            catch (Exception e)
            {
                await StopCoreAsync();
                SetState(SuwayomiState.Failed, e.Message);
                throw new InvalidOperationException($"O servidor de mangás não subiu: {e.Message}", e);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Uri> LaunchAsync(CancellationToken ct)
    {
        KillOrphan();
        Directory.CreateDirectory(paths.Data);
        var port = FreePort();
        var baseUrl = new Uri($"http://127.0.0.1:{port}/");
        SetState(SuwayomiState.Starting, null);

        _log = new StreamWriter(File.Create(paths.Log), Encoding.UTF8) { AutoFlush = true };
        _process = Process.Start(StartInfo(port)) ?? throw new InvalidOperationException("Não foi possível iniciar o Java.");
        if (OperatingSystem.IsWindows())
        {
            var job = new WindowsJob();
            job.Assign(_process);
            _job = job;
            await File.WriteAllTextAsync(paths.PidFile, _process.Id.ToString(CultureInfo.InvariantCulture), ct);
        }
        _process.OutputDataReceived += (_, e) => WriteLog(e.Data);
        _process.ErrorDataReceived += (_, e) => WriteLog(e.Data);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        await WaitUntilReadyAsync(baseUrl, ct);
        BaseUrl = baseUrl;
        SetState(SuwayomiState.Running, null);
        return baseUrl;
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await StopCoreAsync();
            SetState(installer.IsInstalled ? SuwayomiState.Stopped : SuwayomiState.NotInstalled, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void StopNow()
    {
        var process = Interlocked.Exchange(ref _process, null);
        if (process is null)
            return;
        BaseUrl = null;
        try
        {
            if (!OperatingSystem.IsWindows() && !process.HasExited)
                process.StandardInput.Close();
            if (!process.WaitForExit(TimeSpan.FromSeconds(5)))
                process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
        }
        process.Dispose();
        Interlocked.Exchange(ref _job, null)?.Dispose();
        TryDelete(paths.PidFile);
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private async Task StopCoreAsync()
    {
        var process = Interlocked.Exchange(ref _process, null);
        BaseUrl = null;
        if (process is not null)
        {
            try
            {
                if (!process.HasExited)
                {
                    if (OperatingSystem.IsWindows())
                        process.Kill(entireProcessTree: true);
                    else
                        process.StandardInput.Close();
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    try
                    {
                        await process.WaitForExitAsync(timeout.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
            {
            }
            process.Dispose();
        }
        Interlocked.Exchange(ref _job, null)?.Dispose();
        if (_log is not null)
            await _log.DisposeAsync();
        _log = null;
        TryDelete(paths.PidFile);
    }

    private ProcessStartInfo StartInfo(int port)
    {
        var java = new List<string>
        {
            paths.JavaExecutable,
            "-Xmx1g",
            "-Djava.awt.headless=true",
            $"{ConfigPrefix}rootDir={paths.Data}",
            $"{ConfigPrefix}ip=127.0.0.1",
            $"{ConfigPrefix}port={port}",
            $"{ConfigPrefix}webUIEnabled=false",
            $"{ConfigPrefix}initialOpenInBrowserEnabled=false",
            $"{ConfigPrefix}systemTrayEnabled=false",
            $"{ConfigPrefix}kcefEnabled=false",
            $"{ConfigPrefix}globalUpdateInterval=0",
            $"{ConfigPrefix}backupInterval=0",
            $"{ConfigPrefix}webUIUpdateCheckInterval=0",
            "-jar",
            paths.ServerJar,
        };

        ProcessStartInfo info;
        if (OperatingSystem.IsWindows())
        {
            info = new ProcessStartInfo(java[0]);
            foreach (var arg in java.Skip(1))
                info.ArgumentList.Add(arg);
        }
        else
        {
            info = new ProcessStartInfo("/bin/sh") { RedirectStandardInput = true };
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add(Watchdog);
            info.ArgumentList.Add("novelshelf-suwayomi");
            foreach (var arg in java)
                info.ArgumentList.Add(arg);
            info.Environment["NOVELSHELF_PID_FILE"] = paths.PidFile;
        }
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.WorkingDirectory = paths.Root;
        return info;
    }

    private async Task WaitUntilReadyAsync(Uri baseUrl, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        var probe = new Uri(baseUrl, "api/graphql");
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (_process is null || _process.HasExited)
                throw new InvalidOperationException($"o Java encerrou. Veja {paths.Log}");
            try
            {
                using var content = new StringContent("""{"query":"{aboutServer{version}}"}""", Encoding.UTF8, "application/json");
                using var response = await http.PostAsync(probe, content, ct);
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
            }
            await Task.Delay(500, ct);
        }
        throw new TimeoutException($"sem resposta em 90 s. Veja {paths.Log}");
    }

    private void KillOrphan()
    {
        if (!File.Exists(paths.PidFile))
            return;
        try
        {
            if (!int.TryParse(File.ReadAllText(paths.PidFile).Trim(), out var pid))
                return;
            using var process = Process.GetProcessById(pid);
            var ours = OperatingSystem.IsLinux()
                ? File.ReadAllText($"/proc/{pid}/cmdline").Contains(paths.ServerJar, StringComparison.Ordinal)
                : process.ProcessName.Equals("java", StringComparison.OrdinalIgnoreCase);
            if (ours)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(TimeSpan.FromSeconds(5));
            }
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
        }
        finally
        {
            TryDelete(paths.PidFile);
        }
    }

    private void WriteLog(string? line)
    {
        if (line is null)
            return;
        try
        {
            _log?.WriteLine(line);
        }
        catch (Exception e) when (e is ObjectDisposedException or IOException)
        {
        }
    }

    private void SetState(SuwayomiState state, string? error)
    {
        State = state;
        Error = error;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
