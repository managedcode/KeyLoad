using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class FileStorageTransferGate(IOptions<FileStorageOptions> options) : IDisposable
{
    private readonly SemaphoreSlim available = new(options.Value.MaximumTransfers);

    internal async Task<IDisposable> EnterAsync(CancellationToken token)
    {
        if (!await available.WaitAsync(TimeSpan.Zero, token))
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, "The file transfer budget is exhausted.");
        }
        return new Lease(Release);
    }

    private void Release() => available.Release();

    public void Dispose() => available.Dispose();

    private sealed class Lease(Action release) : IDisposable
    {
        private Action? current = release;
        public void Dispose() => Interlocked.Exchange(ref current, null)?.Invoke();
    }
}
