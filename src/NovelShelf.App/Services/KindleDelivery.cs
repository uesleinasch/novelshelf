using NovelShelf.Core.Books;
using NovelShelf.Core.Comics;
using NovelShelf.Core.Library;
using NovelShelf.Devices;

namespace NovelShelf.App.Services;

public sealed record DeliveryResult(int Volumes, int ChapterCount, string TargetFolder, IReadOnlyList<string> Removed);

public sealed class KindleDelivery(AppServices services)
{
    public Task<VolumeExport> ExportAsync(LibraryNovel work, IProgress<ComicExportProgress>? progress = null,
        Func<IReadOnlyList<VolumeSpec>, IEnumerable<VolumeSpec>>? select = null, CancellationToken ct = default) =>
        work.Kind == WorkKind.Comic
            ? services.ComicExporter.ExportVolumesAsync(work.Id, services.KindleProfile, select, progress, ct)
            : services.Exporter.ExportVolumesAsync(work.Id, services.Settings.BookStyle, select, ct);

    public async Task<DeliveryResult> SendAsync(KindleDevice device, LibraryNovel work,
        IProgress<ComicExportProgress>? progress = null, IProgress<string>? status = null, CancellationToken ct = default)
    {
        var everything = work.SentAt is null || work.NeedsResend || work.VolumeLayoutChanged;
        var export = await ExportAsync(work, progress, plan => Volumes.Changed(plan, work.SentChapterCount, everything, work.ResendRanges), ct);

        var removed = new List<string>();
        if (work.VolumeLayoutChanged)
        {
            var baseName = BookExporter.FileNameFor(work);
            var current = export.Plan.Select(v => Volumes.FileName(baseName, v)).ToHashSet();
            removed.AddRange(await services.Sender.RemoveAsync(device,
                name => Volumes.IsFileOf(baseName, name) && !current.Contains(name), ct));
        }

        foreach (var book in export.Books)
        {
            status?.Report(export.Books.Count == 1 ? $"Copiando para {device.Name}..." : $"Copiando {book.KindleFileName} para {device.Name}...");
            await services.Sender.SendAsync(device,
                new SendRequest(book.Azw3Path, book.KindleFileName, book.CoverJpeg, book.ThumbnailFileName), ct);
        }
        services.Library.MarkSent(work.Id, export.TotalChapters);
        return new DeliveryResult(export.Books.Count, export.TotalChapters,
            Path.Combine(device.DocumentsPath, KindleSender.Folder), removed);
    }
}
