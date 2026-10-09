using KeyLoad.Orleans;
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

    private ServerConnectionOperation Acquire(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (closing)
            { throw Errors.Fail(ErrorCode.Cancelled, ServerConnectionProtocol.Closed); }
            if (active >= settings.MaximumConnectionOperations)
            { throw Errors.Fail(ErrorCode.ResourceExhausted, ServerConnectionProtocol.Capacity); }
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
            if (active != ServerConnectionProtocol.NoOperations) { return; }
            if (closing) { drained.TrySetResult(); }
            else { idle.Change(settings.ConnectionIdleTimeout, Timeout.InfiniteTimeSpan); }
        }
    }

    internal void RequestClose()
    {
        lock (gate)
        {
            if (closing) { return; }
            closing = true;
            idle.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            if (active == ServerConnectionProtocol.NoOperations) { drained.TrySetResult(); }
        }
        CompleteClose(ServerConnectionProtocol.Closed);
    }

    private void ExpireIdle()
    {
        lock (gate)
        {
            if (closing || active != ServerConnectionProtocol.NoOperations) { return; }
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
        if (failures.Count == ServerConnectionProtocol.NoFailures) { closeSignal.TrySetResult(); }
        else { closeSignal.TrySetException(failures); }
    }

    internal Task CloseAsync()
    {
        lock (gate)
        { return closeTask ??= CloseCoreAsync(); }
    }

    private async Task CloseCoreAsync()
    {
        await Task.Yield();
        RequestClose();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => closeSignal.Task, failures).ConfigureAwait(false);
        await drained.Task.ConfigureAwait(false);
        if (used)
        {
            await ServerFailureObserver.ObserveAsync(() => services.GetRequiredService<OrleansNode>()
                .CloseConnectionAsync(Id, CancellationToken.None), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(CloseAsync, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => transportClosed.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => idle.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        closed.Dispose();
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
