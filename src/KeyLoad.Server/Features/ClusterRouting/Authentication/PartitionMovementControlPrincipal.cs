using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Resolves control identity from the authenticated native body or its original persisted grant.</summary>
internal static class PartitionMovementControlPrincipal
{
    internal static string Resolve(DatabaseEngine database, PartitionMovePeerEnvelope verified)
        => verified.Stage switch
        {
            PartitionMovePeerStage.ControlPrepare =>
                NativeSerialization.Deserialize<PartitionMovePrepareBody>(verified.Body.Span).OperatorPrincipalId,
            PartitionMovePeerStage.ControlAdvance =>
                NativeSerialization.Deserialize<PartitionMoveAdvanceBody>(verified.Body.Span).OperatorPrincipalId,
            PartitionMovePeerStage.ControlAuthorize =>
                NativeSerialization.Deserialize<PartitionMoveAuthorizeBody>(verified.Body.Span).OperatorPrincipalId,
            PartitionMovePeerStage.ControlAcknowledge => GrantOperator(database, verified),
            PartitionMovePeerStage.ControlAcceptFence =>
                NativeSerialization.Deserialize<PartitionMoveFenceAcceptBody>(verified.Body.Span).OperatorPrincipalId,
            PartitionMovePeerStage.ControlBeginAbort =>
                NativeSerialization.Deserialize<PartitionMoveControlBody>(verified.Body.Span).OperatorPrincipalId,
            PartitionMovePeerStage.ControlFinalizeAbort or PartitionMovePeerStage.ControlCompleteRetirement
                or PartitionMovePeerStage.ControlCancelGrants =>
                NativeSerialization.Deserialize<PartitionMoveCompletionBody>(verified.Body.Span).OperatorPrincipalId,
            PartitionMovePeerStage.ControlFinalize =>
                NativeSerialization.Deserialize<PartitionMoveControlBody>(verified.Body.Span).OperatorPrincipalId,
            PartitionMovePeerStage.ControlAdmitCommand =>
                NativeSerialization.Deserialize<PartitionControlAdmitBody>(verified.Body.Span).OperatorPrincipalId,
            PartitionMovePeerStage.ControlAcknowledgeCommand =>
                NativeSerialization.Deserialize<PartitionControlAcknowledgeBody>(verified.Body.Span).OperatorPrincipalId,
            PartitionMovePeerStage.ControlFinalizeCommand =>
                NativeSerialization.Deserialize<PartitionControlFinalizeBody>(verified.Body.Span).OperatorPrincipalId,
            _ => PartitionStoreProtocol.AdministratorId
        };

    private static string GrantOperator(DatabaseEngine database, PartitionMovePeerEnvelope verified)
    {
        var body = NativeSerialization.Deserialize<PartitionMoveAcknowledgeBody>(verified.Body.Span);
        return database.ResolvePartitionMovementGrantOperator(verified.Partition, verified.MoveId, body.GrantId);
    }
}
