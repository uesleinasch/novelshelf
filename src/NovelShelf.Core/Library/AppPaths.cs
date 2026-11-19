namespace NovelShelf.Core.Library;

public sealed record AppPaths(string Root)
{
    public static AppPaths Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
        "NovelShelf"));

    public string Database => Path.Combine(Root, "library.db");
    public string Covers => Path.Combine(Root, "covers");
    public string Books => Path.Combine(Root, "books");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Covers);
        Directory.CreateDirectory(Books);
    }
}
