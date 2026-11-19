using NovelShelf.Core.Books;
using NovelShelf.Core.Comics;
using NovelShelf.Core.Library;
using NovelShelf.Devices;

namespace NovelShelf.App.Services;

public sealed class KindleDelivery(AppServices services)
{
    public Task<ExportedBook> ExportAsync(LibraryNovel work, IProgress<ComicExportProgress>? progress = null, CancellationToken ct = default) =>
        work.Kind == WorkKind.Comic
            ? services.ComicExporter.ExportAsync(work.Id, services.KindleProfile, progress, ct)
            : services.Exporter.ExportAsync(work.Id, services.Settings.BookStyle, ct);

    public async Task<SendResult> SendAsync(KindleDevice device, ExportedBook book, long workId, CancellationToken ct = default)
    {
        var result = await services.Sender.SendAsync(device,
            new SendRequest(book.Azw3Path, book.KindleFileName, book.CoverJpeg, book.ThumbnailFileName), ct);
        services.Library.MarkSent(workId, book.ChapterCount);
        return result;
    }
}
