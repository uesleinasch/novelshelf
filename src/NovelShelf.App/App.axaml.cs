using Avalonia;
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
            desktop.MainWindow = new MainWindow { DataContext = new MainViewModel(services) };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
