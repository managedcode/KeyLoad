namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Preserves original lifetime, expiry, cancellation and cleanup-failure observations.</summary>
internal static class PartitionMovementNativeLifetime
{
    internal static Task<PartitionMovementGrainServiceReply> WithinLifetimeAsync(PartitionMovementNativeReceiver owner,
        Func<CancellationToken, Task<PartitionMovementGrainServiceReply>> operation, CancellationToken caller)
        => WithinAsync(owner.Routing.Value.ExecutionLifetime, owner.Clock, operation, caller);

    internal static Task<PartitionMovementGrainServiceReply> WithinExpiryAsync(PartitionMovementNativeReceiver owner,
        DateTimeOffset expiry, Func<CancellationToken, Task<PartitionMovementGrainServiceReply>> operation, CancellationToken caller)
    {
        var remaining = expiry - owner.Clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, PartitionMovementProtocol.Unavailable); }
        return WithinAsync(remaining, owner.Clock, operation, caller);
    }

    private static async Task<PartitionMovementGrainServiceReply> WithinAsync(TimeSpan remaining, TimeProvider clock,
        Func<CancellationToken, Task<PartitionMovementGrainServiceReply>> operation, CancellationToken caller)
    {
        PartitionMovementGrainServiceReply? result = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = new CancellationTokenSource(remaining, clock);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, deadline.Token);
                await ServerFailureObserver.ObserveAsync(async () =>
                { result = await operation(linked.Token).ConfigureAwait(false); }, failures).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.Unavailable);
    }
}
