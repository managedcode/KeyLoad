using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;

namespace KeyLoad.Server;

/// <summary>Captures under admitted native read authority and transfers one original retention reservation.</summary>
internal static class PartitionMovementSourceCapture
{
    internal static (PartitionMovementCaptureHandle Handle, PartitionMovementImageSession Session) Capture(
        DatabaseEngine database, ICacheMemoryBudget memory, string localPrincipalId,
        PartitionMovePeerEnvelope verified, ReadExecutionBudget work)
    {
        var body = NativeSerialization.Deserialize<PartitionMoveCaptureRequest>(verified.Body.Span);
        var grant = verified.Grant ?? throw Errors.Fail(ErrorCode.OwnershipLost,
            PartitionMoveProtocol.OwnerMismatch);
        var resourceBytes = NativeSerialization.Measure(grant.Resources);
        var reservation = PartitionMovementImageReservation.Acquire(memory, database.Limits,
            body.MaximumImageBytes, body.MaximumRecords, resourceBytes);
        PartitionMovementImageSession? session = null;
        try
        {
            work.Check();
            var image = database.CaptureVerifiedPartitionMovement(localPrincipalId, verified, work);
            var descriptor = new PartitionMoveImageDescriptor(image.Version, image.MoveId, image.Partition,
                image.SourcePlacement, image.SourceCut, image.Families, image.Digest, image.Resources);
            work.Check();
            var handle = new PartitionMovementCaptureHandle(Guid.NewGuid(), verified.ExpiresAt,
                descriptor, image.Pages.Length);
            session = new(image, reservation);
            return (handle, session);
        }
        catch (Exception error)
        {
            var failures = new List<Exception> { error };
            if (session is not null)
            { ServerFailureObserver.Observe(() => session.DisposeAsync().AsTask().GetAwaiter().GetResult(), failures); }
            else
            { ServerFailureObserver.Observe(reservation.Dispose, failures); }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }
}
