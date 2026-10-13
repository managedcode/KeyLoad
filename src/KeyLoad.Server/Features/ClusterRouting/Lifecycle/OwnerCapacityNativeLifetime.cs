using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class OwnerCapacityNativeLifetime : IAsyncDisposable
{
    private readonly NativeRequestWorkOwner work;
    private readonly OwnerCapacityReceiver receiver;
    private readonly OwnerCapacityResourceSampler sampler;
    private readonly OwnerCapacityPeerAdmission admission;
    private readonly TimeProvider clock;
    private readonly GrainRoutingOptions routing;
    private readonly Lock stopping = new();
    private Task? shutdown;

    internal OwnerCapacityNativeLifetime(IOptions<GrainRoutingOptions> routing, OwnerCapacityReceiver receiver,
        OwnerCapacityResourceSampler sampler, OwnerCapacityPeerAdmission admission, TimeProvider clock)
    {
        this.routing = routing.Value;
        this.receiver = receiver;
        this.sampler = sampler;
        this.admission = admission;
        this.clock = clock;
        work = new(routing);
    }

    internal void Start() => sampler.Start();

    internal async Task<OwnerCapacityNativeReply> ReadAsync(OwnerCapacityNativeRequest request, CancellationToken token)
    {
        var failures = new List<Exception>();
        NativeRequestWorkLease? lease = null;
        CancellationTokenSource? deadline = null;
        CancellationTokenSource? linked = null;
        OwnerCapacityNativeReply? result = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            lease = work.Acquire(request.RequestId, NativeRequestWorkKind.ReadCapability);
            var remaining = request.ExpiresAt - clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            { throw Errors.Fail(ErrorCode.TokenInvalidated, KeyLoad.Core.Features.ClusterRouting.Contracts.TenantPlacementCapacityProtocol.InvalidProof); }
            deadline = new(remaining < routing.ExecutionLifetime ? remaining : routing.ExecutionLifetime, clock);
            linked = CancellationTokenSource.CreateLinkedTokenSource(token, work.ShutdownToken, deadline.Token);
            result = await receiver.ReadAsync(request, linked.Token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(() => linked?.Dispose(), failures);
        ServerFailureObserver.Observe(() => deadline?.Dispose(), failures);
        ServerFailureObserver.Observe(() => lease?.Dispose(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, KeyLoad.Core.Features.ClusterRouting.Contracts.TenantPlacementCapacityProtocol.Unavailable);
    }

    public ValueTask DisposeAsync()
    {
        lock (stopping)
        { return new(shutdown ??= StopCoreAsync()); }
    }

    private async Task StopCoreAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(work.DrainAsync, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => sampler.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(admission.Dispose, failures);
        try
        { await work.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
        { failures.Add(error); }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
        { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
