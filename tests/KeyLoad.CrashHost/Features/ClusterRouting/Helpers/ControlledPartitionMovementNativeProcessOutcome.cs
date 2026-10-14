using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Current authorized native observation of an immutable original phase without redispatch.</summary>
internal static class ControlledPartitionMovementNativeProcessOutcome
{
    private const int FirstVoter = 0;
    private const string Missing = "The genuine movement outcome observation returned no authenticated reply.";

    internal static async Task<PartitionMovementAuthenticatedReply> ReadAsync(
        ControlledPartitionMovementNativeHosts owners, PartitionMovementTransportRequest original,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = owners.Source;
        var limits = source.Database.Limits;
        var retained = original with
        {
            CallerSiloAddress = source.CallerSiloAddress
        };
        var query = new PartitionMovementOutcomeTransportRequest(retained,
            source.Clock.GetUtcNow() + source.Runtime.GrainRouting.Value.RequestLifetime, Guid.NewGuid(),
            Math.Min(limits.MaxBatchBytes, limits.MaxQueryReadBytes),
            Math.Min(limits.MaxBatchMutations, limits.MaxScanRecords), limits.MaxBatchBytes);
        var maximumBytes = Math.Min(limits.MaxBatchBytes, limits.MaxQueryReadBytes);
        if (NativeSerialization.Measure(query) > maximumBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, PartitionMovementProtocol.Unavailable);
        }
        var bytes = NativeSerialization.Serialize(query);
        if (bytes.Length > maximumBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, PartitionMovementProtocol.Unavailable);
        }
        PartitionMovementAuthenticatedReply? reply = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var transport = source.BorrowTransport();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                reply = await transport.ExchangeOutcomeAsync(FirstVoter, owners.Control, query,
                    bytes, local: true, cancellationToken).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return reply ?? throw new InvalidOperationException(Missing);
    }
}
