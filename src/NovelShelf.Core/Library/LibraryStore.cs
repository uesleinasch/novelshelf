using System.Globalization;
using Microsoft.Data.Sqlite;
using NovelShelf.Comics;
using NovelShelf.Core.Comics;
using NovelShelf.Core.Sources;
using NovelShelf.Ebook;

namespace NovelShelf.Core.Library;

public sealed class LibraryStore
{
    private readonly string _connectionString;
    private readonly TimeProvider _time;

    public LibraryStore(string databasePath, TimeProvider? time = null)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
            Pooling = false,
        }.ToString();
        _time = time ?? TimeProvider.System;
        Migrate();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void Migrate()
    {
        using var db = Open();
        Execute(db, """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS novels (
                id INTEGER PRIMARY KEY,
                source_id TEXT NOT NULL,
                slug TEXT NOT NULL,
                url TEXT NOT NULL,
                title TEXT NOT NULL,
                author TEXT,
                synopsis TEXT,
                cover_url TEXT,
                status TEXT,
                genres TEXT NOT NULL DEFAULT '',
                remote_chapter_count INTEGER,
                first_chapter_url TEXT,
                book_id TEXT NOT NULL,
                sent_chapter_count INTEGER NOT NULL DEFAULT 0,
                added_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                sent_at TEXT,
                UNIQUE (source_id, slug)
            );
            CREATE TABLE IF NOT EXISTS chapters (
                novel_id INTEGER NOT NULL REFERENCES novels(id) ON DELETE CASCADE,
                idx INTEGER NOT NULL,
                url TEXT NOT NULL,
                title TEXT NOT NULL,
                html TEXT NOT NULL,
                next_url TEXT,
                downloaded_at TEXT NOT NULL,
                PRIMARY KEY (novel_id, idx)
            );
            """);

        if (ScalarLong(db, "PRAGMA user_version") < 1)
        {
            Execute(db, """
                BEGIN;
                ALTER TABLE novels ADD COLUMN kind TEXT NOT NULL DEFAULT 'novel';
                ALTER TABLE novels ADD COLUMN remote_key TEXT;
                ALTER TABLE novels ADD COLUMN source_name TEXT;
                ALTER TABLE novels ADD COLUMN right_to_left INTEGER NOT NULL DEFAULT 0;
                CREATE TABLE comic_chapters (
                    novel_id INTEGER NOT NULL REFERENCES novels(id) ON DELETE CASCADE,
                    chapter_key TEXT NOT NULL,
                    title TEXT NOT NULL,
                    number REAL NOT NULL,
                    source_order INTEGER NOT NULL,
                    page_count INTEGER NOT NULL,
                    folder TEXT NOT NULL,
                    downloaded_at TEXT NOT NULL,
                    PRIMARY KEY (novel_id, chapter_key)
                );
                PRAGMA user_version = 1;
                COMMIT;
                """);
        }

        if (ScalarLong(db, "PRAGMA user_version") < 2)
        {
            Execute(db, """
                BEGIN;
                ALTER TABLE novels ADD COLUMN comic_layout TEXT;
                PRAGMA user_version = 2;
                COMMIT;
                """);
        }

        if (ScalarLong(db, "PRAGMA user_version") < 3)
        {
            Execute(db, """
                BEGIN;
                ALTER TABLE novels ADD COLUMN book_style TEXT;
                ALTER TABLE novels ADD COLUMN needs_resend INTEGER NOT NULL DEFAULT 0;
                PRAGMA user_version = 3;
                COMMIT;
                """);
        }
    }

    public ComicLayout GetComicLayout(long novelId)
    {
        using var db = Open();
        using var cmd = Command(db, "SELECT comic_layout FROM novels WHERE id = $id", [("$id", novelId)]);
        return ComicLayout.FromJson(cmd.ExecuteScalar() as string);
    }

    public void SetComicLayout(long novelId, ComicLayout layout)
    {
        using var db = Open();
        var json = layout == ComicLayout.Default ? null : layout.Normalized().ToJson();
        Execute(db, """
            UPDATE novels SET comic_layout = $layout,
                needs_resend = CASE WHEN sent_at IS NOT NULL AND comic_layout IS NOT $layout THEN 1 ELSE needs_resend END
            WHERE id = $id
            """, ("$layout", json), ("$id", novelId));
    }

    public BookStyle? GetBookStyle(long novelId)
    {
        using var db = Open();
        using var cmd = Command(db, "SELECT book_style FROM novels WHERE id = $id", [("$id", novelId)]);
        return BookStyle.FromJson(cmd.ExecuteScalar() as string);
    }

    public void SetBookStyle(long novelId, BookStyle? style)
    {
        using var db = Open();
        Execute(db, """
            UPDATE novels SET book_style = $style,
                needs_resend = CASE WHEN sent_at IS NOT NULL AND book_style IS NOT $style THEN 1 ELSE needs_resend END
            WHERE id = $id
            """, ("$style", style?.Normalized().ToJson()), ("$id", novelId));
    }

    public void MarkDefaultStyleChanged()
    {
        using var db = Open();
        Execute(db, "UPDATE novels SET needs_resend = 1 WHERE kind = 'novel' AND book_style IS NULL AND sent_at IS NOT NULL");
    }

    public LibraryNovel UpsertComic(ComicDetails details)
    {
        using var db = Open();
        var now = Stamp(_time.GetUtcNow());
        var existing = ScalarLong(db, "SELECT id FROM novels WHERE kind = 'comic' AND source_id = $source AND remote_key = $key",
            ("$source", details.SourceId), ("$key", details.Key));
        (string, object?)[] fields =
        [
            ("$source", details.SourceId),
            ("$sourceName", details.SourceName),
            ("$key", details.Key),
            ("$url", (details.WebUrl ?? new Uri("about:blank")).ToString()),
            ("$title", details.Title),
            ("$author", details.Author),
            ("$synopsis", details.Description),
            ("$status", details.Status),
            ("$genres", string.Join('\n', details.Genres)),
            ("$count", details.ChapterCount),
            ("$now", now),
        ];

        if (existing > 0)
        {
            Execute(db, """
                UPDATE novels SET source_name = $sourceName, url = $url, title = $title, author = $author,
                    synopsis = $synopsis, status = $status, genres = $genres, remote_chapter_count = $count,
                    updated_at = $now
                WHERE id = $id
                """, [.. fields, ("$id", existing)]);
            return QueryNovels(db, "WHERE n.id = $id", ("$id", existing)).Single();
        }

        Execute(db, """
            INSERT INTO novels (kind, source_id, source_name, remote_key, slug, url, title, author, synopsis, status,
                                genres, remote_chapter_count, book_id, added_at, updated_at)
            VALUES ('comic', $source, $sourceName, $key, $slug, $url, $title, $author, $synopsis, $status,
                    $genres, $count, $bookId, $now, $now)
            """, [.. fields, ("$slug", ComicSlug(details)), ("$bookId", Guid.NewGuid().ToString("N"))]);
        return QueryNovels(db, "WHERE n.id = last_insert_rowid()").Single();
    }

    public void SetRightToLeft(long novelId, bool rightToLeft)
    {
        using var db = Open();
        Execute(db, "UPDATE novels SET right_to_left = $rtl WHERE id = $id", ("$rtl", rightToLeft ? 1 : 0), ("$id", novelId));
    }

    public void AddComicChapter(long novelId, ComicChapterInfo chapter, int pageCount, string folder)
    {
        using var db = Open();
        var now = Stamp(_time.GetUtcNow());
        Execute(db, """
            INSERT INTO comic_chapters (novel_id, chapter_key, title, number, source_order, page_count, folder, downloaded_at)
            VALUES ($novel, $key, $title, $number, $order, $pages, $folder, $now)
            ON CONFLICT (novel_id, chapter_key) DO UPDATE SET
                title = excluded.title, number = excluded.number, source_order = excluded.source_order,
                page_count = excluded.page_count, folder = excluded.folder, downloaded_at = excluded.downloaded_at;
            UPDATE novels SET updated_at = $now WHERE id = $novel;
            """,
            ("$novel", novelId), ("$key", chapter.Key), ("$title", chapter.Name), ("$number", chapter.Number),
            ("$order", chapter.Order), ("$pages", pageCount), ("$folder", folder), ("$now", now));
    }

    public IReadOnlyList<StoredComicChapter> GetComicChapters(long novelId)
    {
        using var db = Open();
        using var cmd = Command(db, """
            SELECT novel_id, chapter_key, title, number, source_order, page_count, folder, downloaded_at
            FROM comic_chapters WHERE novel_id = $novel ORDER BY source_order, number
            """, [("$novel", novelId)]);
        using var r = cmd.ExecuteReader();
        var list = new List<StoredComicChapter>();
        while (r.Read())
        {
            list.Add(new StoredComicChapter(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetDouble(3),
                r.GetInt32(4), r.GetInt32(5), r.GetString(6), ParseStamp(r.GetString(7))));
        }
        return list;
    }

    public static string ComicSlug(ComicDetails details)
    {
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{details.SourceId}\n{details.Key}")))[..8];
        var normalized = details.Title.Normalize(System.Text.NormalizationForm.FormD);
        var slug = new System.Text.StringBuilder();
        foreach (var c in normalized)
        {
            if (char.IsAsciiLetterOrDigit(c))
                slug.Append(char.ToLowerInvariant(c));
            else if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark
                     && slug.Length > 0 && slug[^1] != '-')
                slug.Append('-');
        }
        var text = slug.ToString().Trim('-');
        if (text.Length > 40)
            text = text[..40].TrimEnd('-');
        return text.Length == 0 ? hash : $"{text}-{hash}";
    }

    public LibraryNovel Upsert(NovelDetails details)
    {
        using var db = Open();
        var now = Stamp(_time.GetUtcNow());
        Execute(db, """
            INSERT INTO novels (source_id, slug, url, title, author, synopsis, cover_url, status, genres,
                                remote_chapter_count, first_chapter_url, book_id, added_at, updated_at)
            VALUES ($source, $slug, $url, $title, $author, $synopsis, $cover, $status, $genres,
                    $count, $first, $bookId, $now, $now)
            ON CONFLICT (source_id, slug) DO UPDATE SET
                url = excluded.url,
                title = excluded.title,
                author = excluded.author,
                synopsis = excluded.synopsis,
                cover_url = excluded.cover_url,
                status = excluded.status,
                genres = excluded.genres,
                remote_chapter_count = excluded.remote_chapter_count,
                first_chapter_url = excluded.first_chapter_url,
                updated_at = excluded.updated_at;
            """,
            ("$source", details.SourceId),
            ("$slug", details.Slug),
            ("$url", details.Url.ToString()),
            ("$title", details.Title),
            ("$author", details.Author),
            ("$synopsis", details.Synopsis),
            ("$cover", details.CoverUrl?.ToString()),
            ("$status", details.Status),
            ("$genres", string.Join('\n', details.Genres)),
            ("$count", details.ChapterCount),
            ("$first", details.FirstChapterUrl?.ToString()),
            ("$bookId", Guid.NewGuid().ToString("N")),
            ("$now", now));

        return QueryNovels(db, "WHERE n.source_id = $source AND n.slug = $slug",
            ("$source", details.SourceId), ("$slug", details.Slug)).Single();
    }

    public IReadOnlyList<LibraryNovel> GetNovels()
    {
        using var db = Open();
        return QueryNovels(db, "ORDER BY n.updated_at DESC");
    }

    public LibraryNovel? GetNovel(long id)
    {
        using var db = Open();
        return QueryNovels(db, "WHERE n.id = $id", ("$id", id)).SingleOrDefault();
    }

    public void DeleteNovel(long id)
    {
        using var db = Open();
        Execute(db, "DELETE FROM novels WHERE id = $id", ("$id", id));
    }

    public void AddChapter(long novelId, int index, ChapterPage page)
    {
        using var db = Open();
        var now = Stamp(_time.GetUtcNow());
        Execute(db, """
            INSERT INTO chapters (novel_id, idx, url, title, html, next_url, downloaded_at)
            VALUES ($novel, $idx, $url, $title, $html, $next, $now)
            ON CONFLICT (novel_id, idx) DO UPDATE SET
                url = excluded.url, title = excluded.title, html = excluded.html,
                next_url = excluded.next_url, downloaded_at = excluded.downloaded_at;
            UPDATE novels SET updated_at = $now WHERE id = $novel;
            """,
            ("$novel", novelId),
            ("$idx", index),
            ("$url", page.Url.ToString()),
            ("$title", page.Title),
            ("$html", page.Html),
            ("$next", page.NextUrl?.ToString()),
            ("$now", now));
    }

    public void SetNextUrl(long novelId, int index, Uri? nextUrl)
    {
        using var db = Open();
        Execute(db, "UPDATE chapters SET next_url = $next WHERE novel_id = $novel AND idx = $idx",
            ("$next", nextUrl?.ToString()), ("$novel", novelId), ("$idx", index));
    }

    public StoredChapter? GetFirstChapter(long novelId)
    {
        using var db = Open();
        return QueryChapters(db, "WHERE novel_id = $novel ORDER BY idx LIMIT 1", ("$novel", novelId))
            .SingleOrDefault();
    }

    public StoredChapter? GetLastChapter(long novelId)
    {
        using var db = Open();
        return QueryChapters(db, "WHERE novel_id = $novel ORDER BY idx DESC LIMIT 1", ("$novel", novelId))
            .SingleOrDefault();
    }

    public IReadOnlyList<StoredChapter> GetChapters(long novelId)
    {
        using var db = Open();
        return QueryChapters(db, "WHERE novel_id = $novel ORDER BY idx", ("$novel", novelId));
    }

    public void MarkSent(long novelId, int chapterCount)
    {
        using var db = Open();
        Execute(db, "UPDATE novels SET sent_chapter_count = $count, sent_at = $now, needs_resend = 0 WHERE id = $novel",
            ("$count", chapterCount), ("$now", Stamp(_time.GetUtcNow())), ("$novel", novelId));
    }

    private static List<LibraryNovel> QueryNovels(SqliteConnection db, string tail, params (string, object?)[] args)
    {
        using var cmd = Command(db, $"""
            SELECT n.id, n.source_id, n.slug, n.url, n.title, n.author, n.synopsis, n.cover_url, n.status,
                   n.genres, n.remote_chapter_count, n.first_chapter_url, n.book_id,
                   (SELECT COUNT(*) FROM chapters c WHERE c.novel_id = n.id)
                     + (SELECT COUNT(*) FROM comic_chapters cc WHERE cc.novel_id = n.id),
                   n.sent_chapter_count, n.added_at, n.updated_at, n.sent_at,
                   n.kind, n.remote_key, n.source_name, n.right_to_left, n.needs_resend
            FROM novels n
            {tail}
            """, args);
        using var r = cmd.ExecuteReader();
        var list = new List<LibraryNovel>();
        while (r.Read())
        {
            list.Add(new LibraryNovel(
                Id: r.GetInt64(0),
                SourceId: r.GetString(1),
                Slug: r.GetString(2),
                Url: new Uri(r.GetString(3)),
                Title: r.GetString(4),
                Author: NullableString(r, 5),
                Synopsis: NullableString(r, 6),
                CoverUrl: NullableUri(r, 7),
                Status: NullableString(r, 8),
                Genres: r.GetString(9).Split('\n', StringSplitOptions.RemoveEmptyEntries),
                RemoteChapterCount: r.IsDBNull(10) ? null : r.GetInt32(10),
                FirstChapterUrl: NullableUri(r, 11),
                BookId: r.GetString(12),
                DownloadedChapters: r.GetInt32(13),
                SentChapterCount: r.GetInt32(14),
                AddedAt: ParseStamp(r.GetString(15)),
                UpdatedAt: ParseStamp(r.GetString(16)),
                SentAt: r.IsDBNull(17) ? null : ParseStamp(r.GetString(17)),
                Kind: r.GetString(18) == "comic" ? WorkKind.Comic : WorkKind.Novel,
                RemoteKey: NullableString(r, 19),
                SourceName: NullableString(r, 20),
                RightToLeft: r.GetInt64(21) != 0,
                NeedsResend: r.GetInt64(22) != 0));
        }
        return list;
    }

    private static List<StoredChapter> QueryChapters(SqliteConnection db, string tail, params (string, object?)[] args)
    {
        using var cmd = Command(db, $"SELECT novel_id, idx, url, title, html, next_url, downloaded_at FROM chapters {tail}", args);
        using var r = cmd.ExecuteReader();
        var list = new List<StoredChapter>();
        while (r.Read())
        {
            list.Add(new StoredChapter(
                NovelId: r.GetInt64(0),
                Index: r.GetInt32(1),
                Url: new Uri(r.GetString(2)),
                Title: r.GetString(3),
                Html: r.GetString(4),
                NextUrl: NullableUri(r, 5),
                DownloadedAt: ParseStamp(r.GetString(6))));
        }
        return list;
    }

    private static SqliteCommand Command(SqliteConnection db, string sql, (string Name, object? Value)[] args)
    {
        var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    private static void Execute(SqliteConnection db, string sql, params (string, object?)[] args)
    {
        using var cmd = Command(db, sql, args);
        cmd.ExecuteNonQuery();
    }

    private static long ScalarLong(SqliteConnection db, string sql, params (string, object?)[] args)
    {
        using var cmd = Command(db, sql, args);
        return cmd.ExecuteScalar() is { } value and not DBNull ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : 0;
    }

    private static string? NullableString(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static Uri? NullableUri(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : new Uri(r.GetString(i));
    private static string Stamp(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseStamp(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
