using Avalonia;
using NovelShelf.Core.Library;
using NovelShelf.Core.Platform;

namespace NovelShelf.App;

internal sealed class Program
{
    public static SingleInstance? Instance { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        using var instance = SingleInstance.TryAcquire(AppPaths.Default.Root);
        if (instance is null)
        {
            SingleInstance.SignalExisting(AppPaths.Default.Root, TimeSpan.FromSeconds(2));
            return 0;
        }
        Instance = instance;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
