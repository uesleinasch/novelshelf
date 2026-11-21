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
    public string? Status { get; private set; }

    private void SetStatus(string? status)
    {
        Status = status;
        StateChanged?.Invoke();
    }

    private static string Now => DateTime.Now.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);

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
        SetStatus((candidates.Count == 1 ? "Buscando capítulos novos em 1 obra." : $"Buscando capítulos novos em {candidates.Count} obras.")
            + (_sendWhenDone is { } device ? $" Depois envia ao {device.Name}." : ""));
        return candidates.Count;
    }

    public void OnKindleConnected(KindleDevice device)
    {
        if (!_services.Settings.SendOnConnect)
            return;
        _sendWhenDone = device;
        if (_round is not null)
            SetStatus($"{device.Name} conectado. Envia quando a busca por capítulos novos terminar.");
        else if (StartRound() == 0)
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
        var found = results.Sum(r => r.NewChapters);
        SetStatus(found switch
        {
            0 => $"Busca terminou às {Now}: nenhum capítulo novo.",
            1 => $"Busca terminou às {Now}: 1 capítulo novo.",
            _ => $"Busca terminou às {Now}: {found} capítulos novos.",
        });

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
        if (device is null || _sending)
            return;
        if (!Directory.Exists(device.DocumentsPath))
        {
            SetStatus($"O {device.Name} foi desconectado antes do envio.");
            return;
        }
        var pending = AutoSync.PendingForKindle(_services.Library.GetNovels());
        if (pending.Count == 0)
        {
            SetStatus($"Nada novo para enviar ao {device.Name} ({Now}).");
            return;
        }

        _sending = true;
        var sent = new List<string>();
        var errors = new List<string>();
        try
        {
            for (var i = 0; i < pending.Count; i++)
            {
                var work = pending[i];
                SetStatus($"Convertendo e enviando {i + 1} de {pending.Count} ao {device.Name}: {work.Title}");
                try
                {
                    await _services.Delivery.SendAsync(device, work);
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
            LibraryChanged?.Invoke();
        }

        var title = sent.Count == 1 ? "1 obra enviada, pode ejetar" : $"{sent.Count} obras enviadas, pode ejetar";
        SetStatus(sent.Count == 0
            ? $"Nenhuma obra foi enviada ao {device.Name} ({Now})."
            : $"{title} o {device.Name} ({Now})." + (errors.Count > 0 ? $" {errors.Count} com erro." : ""));
        if (sent.Count > 0)
            Notify(new SyncMessage(title,
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
