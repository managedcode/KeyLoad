using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server;

/// <summary>Owns native source read bytes before a deferred producer or retained session uses them.</summary>
internal static class PartitionMovementTransferSourceScope
{
    internal static PartitionMovementTransferDataCapability Own(PartitionMovementTransferDataCapability query,
        DatabaseLimits limits, PartitionMovementTransferDataAction expected)
    {
        if (query.MaximumReadBytes <= PartitionMovementProtocol.NoReadBytes || query.MaximumReadBytes > limits.MaxQueryReadBytes
            || query.MaximumExaminedRecords <= PartitionMovementProtocol.NoExaminedRecords || query.MaximumExaminedRecords > limits.MaxScanRecords
            || query.MaximumResultBytes <= PartitionMovementProtocol.NoResultBytes || query.MaximumResultBytes > limits.MaxBatchBytes
            || query.Action != expected || query.OriginalAuthorityReplyBytes.IsEmpty
            || query.OriginalAuthorityReplyBytes.Length > limits.MaxBatchBytes
            || string.IsNullOrWhiteSpace(query.OriginalAuthoritySignature)
            || expected == PartitionMovementTransferDataAction.Open && (query.HandleId != Guid.Empty || query.Ordinal != PartitionMovementProtocol.InitialPhaseOrdinal)
            || expected != PartitionMovementTransferDataAction.Open && query.HandleId == Guid.Empty
            || expected == PartitionMovementTransferDataAction.Page && query.Ordinal < PartitionMovementProtocol.InitialPhaseOrdinal
            || expected == PartitionMovementTransferDataAction.Close && query.Ordinal != PartitionMovementProtocol.InitialPhaseOrdinal)
        { throw Errors.Fail(ErrorCode.Unauthenticated, Features.ClusterRouting.PartitionMovementProtocol.InvalidProof); }
        return query with { OriginalAuthorityReplyBytes = query.OriginalAuthorityReplyBytes.ToArray() };
    }
}
