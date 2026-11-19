using Avalonia.Threading;
using NovelShelf.Core.Downloading;
using NovelShelf.Core.Library;
using NovelShelf.Devices;

namespace NovelShelf.App.Services;

public sealed record SyncMessage(string Title, string Text, bool IsError = false);

public sealed class AutoSyncService
{
    private readonly AppServices _services;
    private UpdateRound? _round;
    private KindleDevice? _sendWhenDone;
    private DispatcherTimer? _timer;
    private bool _sending;

    public AutoSyncService(AppServices services)
    {
        _services = services;
        services.Downloads.Updated += OnDownloadsUpdated;
    }

    public event Action<SyncMessage>? Message;
    public event Action? LibraryChanged;
    public event Action? StateChanged;

    public bool IsBusy => _round is not null || _sending;

    public static IReadOnlyList<int> IntervalOptions { get; } = [0, 1, 3, 6, 12, 24];

    public void Start()
    {
        ApplyInterval();
        if (_services.Settings.UpdateOnStart)
            StartRound();
    }

    public void ApplyInterval()
    {
        _timer?.Stop();
        _timer = null;
        var hours = _services.Settings.UpdateIntervalHours;
        if (hours <= 0)
            return;
        _timer = new DispatcherTimer(TimeSpan.FromHours(hours), DispatcherPriority.Background, (_, _) => StartRound());
        _timer.Start();
    }

    public int StartRound()
    {
        if (_round is not null)
            return _round.Count;
        var candidates = AutoSync.UpdateCandidates(_services.Library.GetNovels(), _services.Settings.UpdateComics);
        if (candidates.Count == 0)
            return 0;
        foreach (var work in candidates)
            _services.Downloads.Enqueue(work, AutoSync.UpdateBatch);
        _round = new UpdateRound(candidates.Select(c => c.Id).ToList());
        StateChanged?.Invoke();
        return candidates.Count;
    }

    public void OnKindleConnected(KindleDevice device)
    {
        if (!_services.Settings.SendOnConnect)
            return;
        _sendWhenDone = device;
        if (_round is null && StartRound() == 0)
            _ = SendPendingAsync();
    }

    private void OnDownloadsUpdated()
    {
        if (_round is null)
            return;
        var jobs = _services.Downloads.Queue.Jobs;
        if (!_round.IsFinished(jobs))
            return;
        var results = _round.Results(jobs);
        var failures = _round.Failures(jobs);
        _round = null;
        StateChanged?.Invoke();

        if (results.Count > 0)
        {
            var total = results.Sum(r => r.NewChapters);
            Notify(new SyncMessage(
                total == 1 ? "1 capítulo novo" : $"{total} capítulos novos",
                string.Join(", ", results.Select(r => $"{r.Title} (+{r.NewChapters})"))));
        }
        if (failures.Count > 0)
            Message?.Invoke(new SyncMessage("Algumas obras não atualizaram",
                string.Join("; ", failures.Select(f => $"{f.Title}: {f.Detail}")), IsError: true));
        if (_sendWhenDone is not null)
            _ = SendPendingAsync();
    }

    private async Task SendPendingAsync()
    {
        var device = _sendWhenDone;
        _sendWhenDone = null;
        if (device is null || _sending || !Directory.Exists(device.DocumentsPath))
            return;
        var pending = AutoSync.PendingForKindle(_services.Library.GetNovels());
        if (pending.Count == 0)
            return;

        _sending = true;
        StateChanged?.Invoke();
        var sent = new List<string>();
        var errors = new List<string>();
        try
        {
            foreach (var work in pending)
            {
                try
                {
                    var book = await _services.Delivery.ExportAsync(work);
                    await _services.Delivery.SendAsync(device, book, work.Id);
                    sent.Add(work.Title);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException)
                {
                    errors.Add($"{work.Title}: {e.Message}");
                }
            }
        }
        finally
        {
            _sending = false;
            StateChanged?.Invoke();
            LibraryChanged?.Invoke();
        }

        if (sent.Count > 0)
            Notify(new SyncMessage(
                sent.Count == 1 ? "1 livro atualizado no Kindle" : $"{sent.Count} livros atualizados no Kindle",
                $"{string.Join(", ", sent)}. Ejete o {device.Name} para ele atualizar a biblioteca."));
        if (errors.Count > 0)
            Message?.Invoke(new SyncMessage("Falha ao enviar ao Kindle", string.Join("; ", errors), IsError: true));
    }

    private void Notify(SyncMessage message)
    {
        Message?.Invoke(message);
        DesktopNotifier.Show(message.Title, message.Text);
    }
}
