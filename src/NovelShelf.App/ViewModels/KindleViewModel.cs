using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using NovelShelf.App.Services;
using NovelShelf.Comics;
using NovelShelf.Devices;

namespace NovelShelf.App.ViewModels;

public sealed partial class KindleViewModel(AppServices services) : ViewModelBase
{
    public ObservableCollection<KindleDevice> Devices { get; } = [];
    public IReadOnlyList<KindleProfile> Profiles => KindleProfile.All;

    public KindleProfile Profile
    {
        get => services.KindleProfile;
        set
        {
            if (value is null || value.Id == services.Settings.KindleProfile)
                return;
            services.Settings.KindleProfile = value.Id;
            services.Settings.Save();
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(IsConnected))]
    [NotifyCanExecuteChangedFor(nameof(EjectCommand))]
    private KindleDevice? _selected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EjectCommand))]
    private bool _isEjecting;

    [ObservableProperty]
    private string? _ejectMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EjectSeverity))]
    private bool _ejectFailed;

    public FAInfoBarSeverity EjectSeverity => EjectFailed ? FAInfoBarSeverity.Error : FAInfoBarSeverity.Success;

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

    private bool CanEject() => IsConnected && !IsEjecting;

    [RelayCommand(CanExecute = nameof(CanEject))]
    private async Task EjectAsync()
    {
        try
        {
            EjectMessage = await EjectSelectedAsync();
            EjectFailed = false;
        }
        catch (InvalidOperationException e)
        {
            EjectMessage = e.Message;
            EjectFailed = true;
        }
    }

    public async Task<string> EjectSelectedAsync()
    {
        var device = Selected ?? throw new InvalidOperationException("Nenhum Kindle conectado.");
        IsEjecting = true;
        try
        {
            await services.Ejector.EjectAsync(device);
            Devices.Remove(device);
            Selected = Devices.FirstOrDefault();
            return $"{device.Name} ejetado. Pode desconectar o cabo; o Kindle vai atualizar a biblioteca.";
        }
        finally
        {
            IsEjecting = false;
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
