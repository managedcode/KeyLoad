using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;

namespace KeyLoad.Server;

/// <summary>One fresh read scope; its original descriptor is distinct from the current data-read cut.</summary>
internal sealed class PartitionMovementTransferSourceEntry(PartitionRef partition, Guid moveId, Guid requestId,
    string principalId, PartitionMovementTransferDataCapability query, PartitionMovementTransferHandle handle,
    PartitionMovementImageSession session, NativeRequestWorkLease work) : IPartitionMovementRetainedSourceImage
{
    private readonly PartitionMovementRetainedWorkLease retainedWork = new(work);
    private readonly string authorityDigest = Convert.ToHexStringLower(SHA256.HashData(query.OriginalAuthorityReplyBytes.Span));
    public PartitionRef Partition { get; } = partition;
    public Guid MoveId { get; } = moveId;
    public Guid HandleId => Handle.HandleId;
    public PartitionMovementImageSession Session { get; } = session;
    internal Guid RequestId { get; } = requestId;
    internal PartitionMovementTransferHandle Handle { get; } = handle;

    public void ReleaseWork() => retainedWork.Dispose();
    public async ValueTask DisposeAsync()
    {
        var failures = await PartitionMovementSourceEntryDisposal.ObserveSessionAsync(Session).ConfigureAwait(false);
        try
        { retainedWork.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal void Require(string currentPrincipalId, PartitionMovementTransferDataCapability actual)
    {
        if (currentPrincipalId != principalId || actual.OriginalAuthoritySignature != query.OriginalAuthoritySignature
            || actual.OriginalAuthorityReplyBytes.Length != query.OriginalAuthorityReplyBytes.Length
            || Convert.ToHexStringLower(SHA256.HashData(actual.OriginalAuthorityReplyBytes.Span)) != authorityDigest)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
    }
}
