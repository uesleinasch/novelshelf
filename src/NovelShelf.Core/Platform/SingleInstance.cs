using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace NovelShelf.Core.Platform;

public sealed class SingleInstance : IDisposable
{
    private readonly FileStream _lock;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstance(FileStream lockFile, string pipeName)
    {
        _lock = lockFile;
        _pipeName = pipeName;
    }

    public static SingleInstance? TryAcquire(string root)
    {
        Directory.CreateDirectory(root);
        try
        {
            var lockFile = new FileStream(Path.Combine(root, "app.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new SingleInstance(lockFile, PipeName(root));
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static bool SignalExisting(string root, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return false;
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName(root), PipeDirection.Out, PipeOptions.CurrentUserOnly);
                client.Connect((int)Math.Max(1, remaining.TotalMilliseconds));
                client.WriteByte(1);
                client.Flush();
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(50);
            }
        }
    }

    public void Listen(Action activate)
    {
        RemoveStaleSocket();
        _ = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(_stop.Token);
                    activate();
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException)
                {
                    await Task.Delay(500).ConfigureAwait(false);
                }
            }
        });
    }

    public void Dispose()
    {
        _stop.Cancel();
        _lock.Dispose();
    }

    internal static string PipeName(string root) =>
        "novelshelf-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root))))[..12];

    private void RemoveStaleSocket()
    {
        if (OperatingSystem.IsWindows())
            return;
        try
        {
            File.Delete(Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + _pipeName));
        }
        catch (IOException)
        {
        }
    }
}
