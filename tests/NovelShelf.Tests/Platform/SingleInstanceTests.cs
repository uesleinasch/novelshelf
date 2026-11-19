using NovelShelf.Core.Platform;

namespace NovelShelf.Tests.Platform;

public sealed class SingleInstanceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("novelshelf-single-").FullName;

    [Fact]
    public void Second_copy_does_not_get_the_lock_until_the_first_one_closes()
    {
        var first = SingleInstance.TryAcquire(_dir);
        Assert.NotNull(first);

        Assert.Null(SingleInstance.TryAcquire(_dir));

        first.Dispose();
        using var again = SingleInstance.TryAcquire(_dir);
        Assert.NotNull(again);
    }

    [Fact]
    public async Task Second_copy_asks_the_first_one_to_show_its_window()
    {
        using var first = SingleInstance.TryAcquire(_dir)!;
        var activated = new TaskCompletionSource();
        first.Listen(() => activated.TrySetResult());

        var signaled = await Task.Run(() => SingleInstance.SignalExisting(_dir, TimeSpan.FromSeconds(5)), TestContext.Current.CancellationToken);

        Assert.True(signaled);
        await activated.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Signal_without_a_running_copy_fails_fast()
    {
        Assert.False(SingleInstance.SignalExisting(_dir, TimeSpan.FromMilliseconds(200)));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
