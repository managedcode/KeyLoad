using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class ClusterRestoreSlotValidation
{
    private const int FirstOrdinal = 0;
    private const long FirstPosition = 0;
    private const string Invalid = "The native restore slot is not bound to its original source and target mapping.";

    internal static void RequireSource(ClusterRestoreSlotContext context, StoreIdentity identity, long position,
        ImmutableArray<ClusterRestoreOwnerMapping> mappings)
    {
        ClusterRestoreSlotContextValidation.Require(context);
        if (context.Version != ClusterRestoreSlotContext.CurrentVersion || context.OperationId == Guid.Empty
            || context.SlotOrdinal < FirstOrdinal || context.SourcePosition < FirstPosition
            || context.SourceNodeId != identity.NodeId || context.SourceIncarnation != identity.Incarnation
            || context.SourcePosition != position || context.TargetNodeId == Guid.Empty
            || context.TargetNodeId == identity.NodeId || context.TargetIncarnation == Guid.Empty
            || context.TargetIncarnation == identity.Incarnation || string.IsNullOrWhiteSpace(context.PlanDigest)
            || string.IsNullOrWhiteSpace(context.SourceEnvelopeDigest)
            || string.IsNullOrWhiteSpace(context.TargetSignerFingerprint) || mappings.IsDefault
            || !mappings.Any(mapping => mapping.Source.Incarnation == identity.Incarnation
                && mapping.Target.Incarnation == context.TargetIncarnation))
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
    }
}
