using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using NovelShelf.App.Services;
using NovelShelf.Comics;
using NovelShelf.Devices;
using Avalonia.Threading;

namespace NovelShelf.App.ViewModels;

public sealed partial class KindleViewModel(AppServices services, Action openBookStyle) : ViewModelBase
{
    private readonly HashSet<string> _ejected = [];
    private DispatcherTimer? _watch;
    private bool _polling;

    public event Action<KindleDevice>? Connected;

    public IReadOnlyList<Choice<int>> IntervalOptions { get; } = AutoSyncService.IntervalOptions
        .Select(h => new Choice<int>(h == 0 ? "Só ao abrir o app" : h == 1 ? "A cada 1 hora" : $"A cada {h} horas", h))
        .ToList();

    public bool UpdateOnStart
    {
        get => services.Settings.UpdateOnStart;
        set => SaveSetting(() => services.Settings.UpdateOnStart = value);
    }

    public Choice<int>? UpdateInterval
    {
        get => IntervalOptions.FirstOrDefault(o => o.Value == services.Settings.UpdateIntervalHours) ?? IntervalOptions[0];
        set
        {
            if (value is null)
                return;
            SaveSetting(() => services.Settings.UpdateIntervalHours = value.Value);
            services.AutoSync.ApplyInterval();
        }
    }

    public bool UpdateComics
    {
        get => services.Settings.UpdateComics;
        set => SaveSetting(() => services.Settings.UpdateComics = value);
    }

    public bool SendOnConnect
    {
        get => services.Settings.SendOnConnect;
        set => SaveSetting(() => services.Settings.SendOnConnect = value);
    }

    [ObservableProperty]
    private string? _syncMessage;

    private void SaveSetting(Action apply, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        apply();
        services.Settings.Save();
        OnPropertyChanged(property);
    }

    [RelayCommand]
    private void CheckNow()
    {
        var count = services.AutoSync.StartRound();
        SyncMessage = count == 0
            ? "Nenhuma obra para atualizar. Só entram as obras já baixadas até o último capítulo."
            : $"Procurando capítulos novos em {count} obras. Acompanhe na página Downloads.";
    }

    public void StartWatching()
    {
        _watch ??= new DispatcherTimer(TimeSpan.FromSeconds(15), DispatcherPriority.Background, async (_, _) => await PollAsync());
        _watch.Start();
    }

    private async Task PollAsync()
    {
        if (_polling || IsSearching || IsEjecting)
            return;
        _polling = true;
        try
        {
            UpdateDevices(await services.Locator.FindAsync());
        }
        finally
        {
            _polling = false;
        }
    }

    private void UpdateDevices(IReadOnlyList<KindleDevice> found)
    {
        _ejected.IntersectWith(found.Select(d => d.RootPath));
        var known = Devices.Select(d => d.RootPath).ToHashSet();
        var arrived = found.Where(d => !known.Contains(d.RootPath) && !_ejected.Contains(d.RootPath)).ToList();
        var visible = found.Where(d => !_ejected.Contains(d.RootPath)).ToList();
        if (!visible.Select(d => d.RootPath).SequenceEqual(Devices.Select(d => d.RootPath)))
        {
            Devices.Clear();
            foreach (var device in visible)
                Devices.Add(device);
            Selected = Devices.FirstOrDefault(d => d.RootPath == Selected?.RootPath) ?? Devices.FirstOrDefault();
        }
        foreach (var device in arrived)
            Connected?.Invoke(device);
    }

    [RelayCommand]
    private void OpenBookStyle() => openBookStyle();

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
            _ejected.Clear();
            UpdateDevices(await services.Locator.FindAsync());
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
            _ejected.Add(device.RootPath);
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
