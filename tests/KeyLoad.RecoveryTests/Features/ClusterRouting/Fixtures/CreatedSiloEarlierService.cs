using Microsoft.Extensions.Hosting;

namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

internal sealed class CreatedSiloEarlierService(string path,
    CancellationTokenSource selected) : IHostedLifecycleService, IAsyncDisposable
{
    private readonly Lock sync = new();
    private FileStream? owned;
    private Task? closing;
    internal CancellationToken StartupToken { get; private set; }
    internal bool Opened { get; private set; }
    internal bool FileReleased { get; private set; }
    internal bool StopEntered { get; private set; }
    internal bool StartEntered { get; private set; }

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        StartupToken = cancellationToken;
        owned = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        Opened = true;
        await selected.CancelAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        StartEntered = true;
        return Task.CompletedTask;
    }

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        StopEntered = true;
        await DisposeAsync().ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        lock (sync)
        {
            closing ??= CloseOriginalAsync();
            return new ValueTask(closing);
        }
    }

    private async Task CloseOriginalAsync()
    {
        if (owned is not null)
        { await owned.DisposeAsync().ConfigureAwait(false); }
        owned = null;
        FileReleased = true;
    }
}
