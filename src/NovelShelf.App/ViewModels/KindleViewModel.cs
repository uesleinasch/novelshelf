using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelShelf.App.Services;
using NovelShelf.Devices;

namespace NovelShelf.App.ViewModels;

public sealed partial class KindleViewModel(AppServices services) : ViewModelBase
{
    public ObservableCollection<KindleDevice> Devices { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(IsConnected))]
    private KindleDevice? _selected;

    [ObservableProperty]
    private bool _isSearching;

    public bool IsConnected => Selected is not null;

    public string Summary => Selected is { } d
        ? $"{d.Name} ({(d.Connection == KindleConnection.Mtp ? "MTP" : "USB")})"
        : "Nenhum Kindle conectado";

    [RelayCommand]
    public async Task DetectAsync()
    {
        IsSearching = true;
        try
        {
            var found = await services.Locator.FindAsync();
            Devices.Clear();
            foreach (var device in found)
                Devices.Add(device);
            Selected = Devices.FirstOrDefault(d => d.RootPath == Selected?.RootPath) ?? Devices.FirstOrDefault();
        }
        finally
        {
            IsSearching = false;
        }
    }

    public async Task<KindleDevice?> EnsureDeviceAsync()
    {
        if (Selected is { } current && Directory.Exists(current.DocumentsPath))
            return current;
        await DetectAsync();
        return Selected;
    }
}
