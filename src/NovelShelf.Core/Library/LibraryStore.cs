using System.Globalization;
using Microsoft.Data.Sqlite;
using NovelShelf.Core.Sources;

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
        Execute(db, "UPDATE novels SET sent_chapter_count = $count, sent_at = $now WHERE id = $novel",
            ("$count", chapterCount), ("$now", Stamp(_time.GetUtcNow())), ("$novel", novelId));
    }

    private static List<LibraryNovel> QueryNovels(SqliteConnection db, string tail, params (string, object?)[] args)
    {
        using var cmd = Command(db, $"""
            SELECT n.id, n.source_id, n.slug, n.url, n.title, n.author, n.synopsis, n.cover_url, n.status,
                   n.genres, n.remote_chapter_count, n.first_chapter_url, n.book_id,
                   (SELECT COUNT(*) FROM chapters c WHERE c.novel_id = n.id),
                   n.sent_chapter_count, n.added_at, n.updated_at, n.sent_at
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
                SentAt: r.IsDBNull(17) ? null : ParseStamp(r.GetString(17))));
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

    private static string? NullableString(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static Uri? NullableUri(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : new Uri(r.GetString(i));
    private static string Stamp(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseStamp(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
