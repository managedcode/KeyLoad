using System.Runtime.ExceptionServices;
using ManagedCode.Communication.CQRS;
using ManagedCode.MCPGateway.Abstractions;

namespace KeyLoad.Server;

/// <summary>Drains active graph calls before disposing the factory-owned native gateway instance.</summary>
internal sealed class McpGatewayCatalogLifetime : IAsyncDisposable
{
    private const string IndexUnavailable = "The canonical MCP metadata index is unavailable.";
    private readonly Func<CancellationToken, Task<IMcpGatewayInstance>> _createInstance;
    private readonly Action _releaseLease;
    private readonly Lock _sync = new();
    private IMcpGatewayInstance? _instance;
    private Task? _initialization;
    private Task? _disposal;
    private TaskCompletionSource? _drained;
    private int _active;
    private bool _closing;

    internal McpGatewayCatalogLifetime(Func<CancellationToken, Task<IMcpGatewayInstance>> createInstance)
    {
        ArgumentNullException.ThrowIfNull(createInstance);
        _createInstance = createInstance;
        _releaseLease = Release;
    }

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
            return new Lease(_releaseLease, _instance);
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
        var instance = await _createInstance(cancellationToken).ConfigureAwait(false);
        bool rejected;
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
        const int EmptyActive = 0;

        Task? initialization;
        Task drained;
        lock (_sync)
        {
            _closing = true;
            initialization = _initialization;
            drained = _active == EmptyActive
                ? Task.CompletedTask
                : (_drained ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }

        if (initialization is not null)
        {
            // Startup owns nonfatal initialization errors; disposal still joins the original task.
            await initialization.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (initialization.Exception is { } error && CqrsRuntimeFailures.FindFatal(error) is { } fatal)
            {
                ExceptionDispatchInfo.Capture(fatal).Throw();
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
        const int EmptyActive = 0;

        lock (_sync)
        {
            _active--;
            if (_active == EmptyActive)
            {
                _drained?.TrySetResult();
            }
        }
    }

    internal sealed class Lease(Action release, IMcpGatewayInstance instance) : IDisposable
    {
        private Action? _release = release;
        internal IMcpGatewayInstance Instance { get; } = instance;

        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
