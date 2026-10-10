using System.Threading.Channels;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class PhysicalOwnerRegistrationWorker(ISiloStatusOracle oracle, OrleansNode node,
    PhysicalOwnerRegistrationController controller, IOptions<GrainRoutingOptions> routingOptions,
    TimeProvider clock, ILogger logger) : ISiloStatusListener, IAsyncDisposable
{
    private const int SignalCapacity = 1;
    private const string SubscriptionUnavailable = "Physical owner registration could not subscribe to native membership.";
    private readonly Channel<bool> signals = Channel.CreateBounded<bool>(new BoundedChannelOptions(SignalCapacity)
    { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.DropWrite });
    private readonly CancellationTokenSource stopping = new();
    private readonly Lock gate = new();
    private Task? worker;
    private Task? shutdown;
    private bool subscribed;

    internal void Start()
    {
        lock (gate)
        {
            if (worker is not null || shutdown is not null)
            { throw new InvalidOperationException(SubscriptionUnavailable); }
            subscribed = oracle.SubscribeToSiloStatusEvents(this);
            if (!subscribed)
            { throw Errors.Fail(ErrorCode.OwnershipLost, SubscriptionUnavailable); }
            worker = RunAsync();
        }
    }

    public void SiloStatusChangeNotification(SiloAddress updatedSilo, SiloStatus status)
    {
        _ = updatedSilo;
        _ = status;
        signals.Writer.TryWrite(true);
    }

    private async Task RunAsync()
    {
        var expiresAt = clock.GetUtcNow() + routingOptions.Value.ExecutionLifetime;
        using var deadline = new CancellationTokenSource(routingOptions.Value.ExecutionLifetime, clock);
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token, deadline.Token);
        try
        {
            while (await node.MembershipReadyAsync(execution.Token).ConfigureAwait(false) is null)
            { await signals.Reader.ReadAsync(execution.Token).ConfigureAwait(false); }
            await controller.RegisterAsync(expiresAt, execution.Token).ConfigureAwait(false);
            PhysicalOwnerRegistrationLog.Succeeded(logger);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested && !deadline.IsCancellationRequested)
        { }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            var observation = controller.ObserveFailure(deadline, stopping);
            var failures = new List<Exception> { error };
            ServerFailureObserver.Observe(() => PhysicalOwnerRegistrationLog.Failed(logger, error,
                observation.Stage, observation.DeadlineCancelled, observation.StoppingCancelled, observation.LastProbeHttpStatus), failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        { shutdown ??= StopAsync(worker); return new ValueTask(shutdown); }
    }

    private async Task StopAsync(Task? original)
    {
        var failures = new List<Exception>();
        if (subscribed)
        {
            ServerFailureObserver.Observe(() =>
            {
                if (!oracle.UnSubscribeFromSiloStatusEvents(this))
                { throw Errors.Fail(ErrorCode.OwnershipLost, SubscriptionUnavailable); }
            }, failures);
            subscribed = false;
        }
        await ServerFailureObserver.ObserveAsync(stopping.CancelAsync, failures).ConfigureAwait(false);
        if (original is not null)
        { await ServerFailureObserver.ObserveAsync(() => original, failures).ConfigureAwait(false); }
        try
        { stopping.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
