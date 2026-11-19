namespace NovelShelf.Suwayomi;

public sealed record RuntimeArtifact(string Name, Uri Url, string Sha256, long ApproximateBytes);

public static class SuwayomiRelease
{
    public const string ServerVersion = "v2.4.2366";
    public const string JavaVersion = "21.0.13";
    public const string NoxIndexUrl = "https://raw.githubusercontent.com/ProjectNox-org/manga-extensoes/repo/index.min.json";
    public const string NoxRepositoryPrefix = "https://raw.githubusercontent.com/ProjectNox-org/manga-extensoes/repo/";

    public static RuntimeArtifact Server { get; } = new(
        "Suwayomi-Server",
        new Uri($"https://github.com/Suwayomi/Suwayomi-Server/releases/download/{ServerVersion}/Suwayomi-Server-{ServerVersion}.jar"),
        "af9feb20af9d7ebe9e30769e6c6ebc7fc7ab1c447388d42e0280b1da5fe07bfd",
        183_000_000);

    public static RuntimeArtifact Java { get; } = OperatingSystem.IsWindows()
        ? new(
            "Java 21 (Temurin)",
            new Uri("https://github.com/adoptium/temurin21-binaries/releases/download/jdk-21.0.13%2B9/OpenJDK21U-jre_x64_windows_hotspot_21.0.13_9.zip"),
            "2332c0b4cff6bed722da3567c91d5e47568bcba863fa4945d0a8d6fc76158628",
            48_000_000)
        : new(
            "Java 21 (Temurin)",
            new Uri("https://github.com/adoptium/temurin21-binaries/releases/download/jdk-21.0.13.1%2B2/OpenJDK21U-jre_x64_linux_hotspot_21.0.13.1_2.tar.gz"),
            "18f44fab9065217f3ba61d0eef8a7b5f3c3713aa7293001b0d4ac10ede3850f0",
            52_000_000);
}

public sealed record SuwayomiPaths(string Root)
{
    // ClassGraph (used by Suwayomi) treats the parent of a java.home named "jre" as part of the JDK and skips
    // any jar inside it; the server then starts without its GraphQL classes.
    public string Java => Path.Combine(Root, "java");
    public string JavaExecutable => Path.Combine(Java, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");
    public string ServerJar => Path.Combine(Root, "server", "Suwayomi-Server.jar");
    public string Data => Path.Combine(Root, "data");
    public string Log => Path.Combine(Root, "server.log");
    public string PidFile => Path.Combine(Root, "server.pid");
}
