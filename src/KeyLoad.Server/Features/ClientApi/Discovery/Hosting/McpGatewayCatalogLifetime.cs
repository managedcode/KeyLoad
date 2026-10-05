using ManagedCode.MCPGateway.Abstractions;

namespace KeyLoad.Server;

/// <summary>Drains active graph calls before disposing the factory-owned native gateway instance.</summary>
internal sealed class McpGatewayCatalogLifetime(
    Func<CancellationToken, Task<IMcpGatewayInstance>> createInstance) : IAsyncDisposable
{
    private const string IndexUnavailable = "The canonical MCP metadata index is unavailable.";
    private readonly object _sync = new();
    private IMcpGatewayInstance? _instance;
    private Task? _initialization;
    private Task? _disposal;
    private TaskCompletionSource? _drained;
    private int _active;
    private bool _closing;

    internal Task InitializeAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            return _initialization ??= InitializeCoreAsync(cancellationToken);
        }
    }

    internal Lease Acquire()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            if (_instance is null || _initialization?.IsCompletedSuccessfully != true)
            {
                throw new InvalidOperationException(IndexUnavailable);
            }

            _active++;
            return new Lease(this, _instance);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            return new ValueTask(_disposal ??= DisposeCoreAsync());
        }
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        var instance = await createInstance(cancellationToken).ConfigureAwait(false);
        var rejected = false;
        lock (_sync)
        {
            rejected = _closing;
            if (!rejected)
            {
                _instance = instance;
            }
        }

        if (rejected)
        {
            await instance.DisposeAsync().ConfigureAwait(false);
            ObjectDisposedException.ThrowIf(true, this);
        }
    }

    private async Task DisposeCoreAsync()
    {
        Task? initialization;
        Task drained;
        lock (_sync)
        {
            _closing = true;
            initialization = _initialization;
            drained = _active == 0
                ? Task.CompletedTask
                : (_drained ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }

        if (initialization is not null)
        {
            try
            {
                await initialization.ConfigureAwait(false);
            }
            catch (Exception) when (initialization.IsFaulted || initialization.IsCanceled)
            {
                // BuildInstance owns failed-attempt cleanup; InitializeAsync reports the primary failure.
                await drained.ConfigureAwait(false);
                return;
            }
        }

        await drained.ConfigureAwait(false);
        IMcpGatewayInstance? instance;
        lock (_sync)
        {
            instance = _instance;
            _instance = null;
        }

        if (instance is not null)
        {
            await instance.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void Release()
    {
        lock (_sync)
        {
            _active--;
            if (_active == 0)
            {
                _drained?.TrySetResult();
            }
        }
    }

    internal sealed class Lease(McpGatewayCatalogLifetime owner, IMcpGatewayInstance instance) : IDisposable
    {
        private McpGatewayCatalogLifetime? _owner = owner;
        internal IMcpGatewayInstance Instance { get; } = instance;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }
}
