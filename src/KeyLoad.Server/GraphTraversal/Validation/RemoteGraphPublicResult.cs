using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.GraphTraversal;

internal static class RemoteGraphPublicResult
{
    internal static void RequireComplete(OperationKind? kind, Guid originalCommandId,
        GrainOperationReply actualReply, object? actualValue)
    {
        if (!RemoteGraphGatewaySigning.HandlesCommand(kind) || actualReply.Error is not null)
        {
            return;
        }
        if (actualValue is not RemoteGraphEdgeResultV1 result || result.CommandId != originalCommandId
            || originalCommandId == Guid.Empty || result.SourceReceipt is null
            || result.Deliveries.IsDefaultOrEmpty)
        {
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteGraphGatewaySigning.Interrupted);
        }
        foreach (var delivery in result.Deliveries)
        {
            if (delivery is null || delivery.Destination is null
                || delivery.TargetReceipt is null || delivery.CompletionReceipt is null)
            {
                throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteGraphGatewaySigning.Interrupted);
            }
        }
    }
}
