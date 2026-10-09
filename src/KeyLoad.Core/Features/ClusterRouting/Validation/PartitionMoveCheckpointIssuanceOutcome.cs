using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    /// <summary>Verifies the stored epoch against the actual native first admission or grant outcome, never current policy.</summary>
    private void RequireMoveParentOriginalIssuance(IKeyValueView view, PrincipalRecord principal,
        PartitionMoveParentPhase original)
    {
        if (!principal.ClusterAdministrator)
        { throw Errors.Fail(ErrorCode.PermissionDenied, ClusterAdministrationRequiredMessage); }
        var issuedGrant = original.OriginalGrant;
        var issuedId = issuedGrant?.GrantId ?? original.AdmissionCheckpointReceipt.CommandId;
        var receipt = issuedGrant is null ? original.AdmissionCheckpointReceipt : original.OriginalAuthorization
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        var selected = CommandOutcomeKeyResolver.Select(view, principal.Id, issuedId,
            new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, original.Partition));
        var stored = selected.Outcome
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (stored.Incarnation != Store.Identity.Incarnation || stored.PolicyEpoch != original.OriginalIssuancePolicyEpoch
            || stored.Result.Error is not null || receipt.CommandId != issuedId)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority); }
        var actual = stored.Result.Get<PartitionMovePhaseResult>();
        var expected = issuedGrant is null ? PartitionMovePeerStage.ControlCheckpoint : PartitionMovePeerStage.ControlAuthorize;
        if (actual.Stage != expected || actual.MoveId != original.MoveId
            || !NativeSerialization.Serialize(actual.Journal).AsSpan().SequenceEqual(NativeSerialization.Serialize(receipt))
            || issuedGrant is not null && !NativeSerialization.Serialize(actual.Grant).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(issuedGrant)))
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority); }
    }
}
