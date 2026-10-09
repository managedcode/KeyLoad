using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Uses the shared source-read producer lifetime for the actual first Capture effect scope.</summary>
internal static class PartitionMovementCaptureProducer
{
    internal static (PartitionMovementCaptureHandle? Handle, Exception? Failure) Run(
        DatabaseEngine database, ICacheMemoryBudget memory, NativeRequestWorkOwner workOwner,
        IOptions<DatabaseLimits> limits, TimeProvider clock,
        PrincipalRecord principal, PartitionMovePeerEnvelope verified, PartitionMovementPendingCapture producer,
        Action<PartitionMovementCaptureHandle, PartitionMovementImageSession, NativeRequestWorkLease> publish,
        CancellationToken shutdown, CancellationToken cancellationToken)
        => PartitionMovementSourceReadProducer.Run(workOwner, limits, clock, producer,
            work => PartitionMovementSourceCapture.Capture(database, memory, principal.Id, verified, work),
            publish, shutdown, cancellationToken);
}
