using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server;

/// <summary>Admits original image retention before constructing current verified transfer pages.</summary>
internal static class PartitionMovementTransferSourceCapture
{
    internal static (PartitionMovementTransferHandle Handle, PartitionMovementImageSession Session) Capture(
        DatabaseEngine database, ICacheMemoryBudget memory, string principalId,
        PartitionMovementTransferDataCapability query, ReadExecutionBudget work)
    {
        var authority = database.VerifyPartitionMovementTransferReadAuthority(principalId,
            query.OriginalAuthorityReplyBytes, query.OriginalAuthoritySignature, work);
        var request = NativeSerialization.Deserialize<PartitionMoveCaptureRequest>(authority.CapturePhase.OriginalPhase!.Body.Span);
        var originalReply = NativeSerialization.Deserialize<PartitionMovementTransferAuthorityReply>(query.OriginalAuthorityReplyBytes.Span);
        var reservation = PartitionMovementImageReservation.Acquire(memory, database.Limits,
            request.MaximumImageBytes, request.MaximumRecords, query.OriginalAuthorityReplyBytes.Length);
        PartitionMovementImageSession? session = null;
        try
        {
            var current = database.ReadVerifiedPartitionMovementTransferData(principalId,
                query.OriginalAuthorityReplyBytes, query.OriginalAuthoritySignature, work);
            var descriptor = current.OriginalDescriptor;
            var handle = new PartitionMovementTransferHandle(Guid.NewGuid(), originalReply.ExpiresAt, descriptor,
                current.Pages.Length, current.CurrentReadCut, work.ReadBytes, current.Pages.Sum(page => page.Records.Length));
            var pages = new PartitionMoveImage(descriptor.Version, descriptor.MoveId, descriptor.Partition,
                descriptor.SourcePlacement, descriptor.SourceCut, descriptor.Families, current.Pages,
                descriptor.Digest, descriptor.Resources);
            session = new(pages, reservation);
            work.CheckResult(handle);
            return (handle, session);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            if (session is not null)
            { ServerFailureObserver.Observe(() => session.DisposeAsync().AsTask().GetAwaiter().GetResult(), failures); }
            else
            { ServerFailureObserver.Observe(reservation.Dispose, failures); }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }
}
