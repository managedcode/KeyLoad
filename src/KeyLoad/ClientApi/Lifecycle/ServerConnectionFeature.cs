using KeyLoad.Clustering;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class ServerConnectionFeature : IAsyncDisposable
{
    private readonly Lock gate = new();
    private readonly BaseConnectionContext transport;
    private readonly IServiceProvider services;
    private readonly GrainRoutingOptions settings;
    private readonly CancellationTokenSource closed = new();
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource closeSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ITimer idle;
    private readonly TimeProvider clock;
    private long lastIdleTimestamp;
    private readonly CancellationTokenRegistration transportClosed;
    private Task? closeTask;
    private int active;
    private bool closing;
    private bool used;

    internal ServerConnectionFeature(BaseConnectionContext transport, IServiceProvider services,
        IOptions<GrainRoutingOptions> options, TimeProvider clock)
    {
        this.transport = transport;
        this.services = services;
        settings = options.Value;
        this.clock = clock;
        lastIdleTimestamp = clock.GetTimestamp();
        idle = clock.CreateTimer(static state => ((ServerConnectionFeature)state!).ExpireIdle(),
            this, settings.ConnectionIdleTimeout, Timeout.InfiniteTimeSpan);
        transportClosed = transport.ConnectionClosed.Register(static state =>
            ((ServerConnectionFeature)state!).RequestClose(), this);
    }

    internal Guid Id { get; } = Guid.NewGuid();
    internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal static ServerConnectionOperation Acquire(HttpContext context, CancellationToken cancellationToken)
        => (context.Features.Get<ServerConnectionFeature>()
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, ServerConnectionProtocol.Unavailable))
            .Acquire(cancellationToken);

    internal ServerConnectionOperation Acquire(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (closing)
            {
                throw Errors.Fail(ErrorCode.Cancelled, ServerConnectionProtocol.Closed);
            }
            var operation = new ServerConnectionOperation(this, cancellationToken, closed.Token);
            ++active;
            used = true;
            idle.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return operation;
        }
    }

    internal void Release()
    {
        lock (gate)
        {
            --active;
            if (active != ServerConnectionProtocol.NoOperations)
            {
                return;
            }
            if (closing)
            {
                drained.TrySetResult();
            }
            else
            {
                lastIdleTimestamp = clock.GetTimestamp();
                idle.Change(settings.ConnectionIdleTimeout, Timeout.InfiniteTimeSpan);
            }
        }
    }

    internal void RequestClose()
    {
        lock (gate)
        {
            if (closing)
            {
                return;
            }
            closing = true;
            idle.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            if (active == ServerConnectionProtocol.NoOperations)
            {
                drained.TrySetResult();
            }
        }
        CompleteClose(ServerConnectionProtocol.Closed);
    }

    private void ExpireIdle()
    {
        lock (gate)
        {
            if (closing || active != ServerConnectionProtocol.NoOperations)
            {
                return;
            }
            var remaining = settings.ConnectionIdleTimeout - clock.GetElapsedTime(lastIdleTimestamp);
            if (remaining > TimeSpan.Zero)
            {
                idle.Change(remaining, Timeout.InfiniteTimeSpan);
                return;
            }
            closing = true;
            drained.TrySetResult();
        }
        CompleteClose(ServerConnectionProtocol.Idle);
    }

    private void CompleteClose(string message)
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(closed.Cancel, failures);
        ServerFailureObserver.Observe(() => transport.Abort(new ConnectionAbortedException(message)), failures);
        if (failures.Count == ServerConnectionProtocol.NoFailures)
        {
            closeSignal.TrySetResult();
        }
        else
        {
            closeSignal.TrySetException(failures);
        }
    }

    internal Task CloseAsync()
    {
        lock (gate)
        {
            return closeTask ??= CloseCoreAsync();
        }
    }

    private async Task CloseCoreAsync()
    {
        await Task.Yield();
        RequestClose();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => closeSignal.Task, failures);
        await drained.Task;
        if (used)
        {
            await ServerFailureObserver.ObserveAsync(() => services.GetRequiredService<OrleansNode>()
                .CloseConnectionAsync(Id, CancellationToken.None), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(CloseAsync, failures);
        await ServerFailureObserver.ObserveAsync(() => transportClosed.DisposeAsync().AsTask(), failures);
        await ServerFailureObserver.ObserveAsync(() => idle.DisposeAsync().AsTask(), failures);
        closed.Dispose();
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
