using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using NovelShelf.App.Services;
using NovelShelf.App.ViewModels;
using NovelShelf.App.Views;
using NovelShelf.Core.Library;

namespace NovelShelf.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new AppServices(AppPaths.Default);
            desktop.ShutdownRequested += (_, _) => services.Manga.Server.StopNow();
            desktop.Exit += (_, _) => services.Manga.Server.StopNow();
            AppDomain.CurrentDomain.ProcessExit += (_, _) => services.Manga.Server.StopNow();
            var window = new MainWindow { DataContext = new MainViewModel(services) };
            desktop.MainWindow = window;
            Program.Instance?.Listen(() => Dispatcher.UIThread.Post(() =>
            {
                if (window.WindowState == WindowState.Minimized)
                    window.WindowState = WindowState.Normal;
                window.Show();
                window.Activate();
            }));
            if (services.Manga.IsInstalled)
                _ = StartMangaServerAsync(services.Manga);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task StartMangaServerAsync(MangaBackend manga)
    {
        try
        {
            await manga.EnsureStartedAsync();
        }
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
        }
    }
}
