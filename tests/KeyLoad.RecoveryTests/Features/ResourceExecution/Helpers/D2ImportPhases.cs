using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.CrashHost.Features.ResourceExecution;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal static class D2ImportPhases
{
    internal static PartitionMovePhaseCommand Phase(D2ImportSession session, PartitionMovePhaseResult state,
        PartitionMovePeerStage stage, ReadOnlyMemory<byte> body, int ordinal = D2ImportCrashProtocol.Empty)
    {
        var control = state.Control ?? throw new InvalidOperationException("The real native control result is absent.");
        return new(D2ImportCrashProtocol.Version, control.MoveId, control.Partition, session.Owners.Control.Owner,
            control.SourcePlacement, session.Owners.Destination.Owner, state.Journal.ControlIntentDigest,
            stage, ordinal, body, Resources: []);
    }

    internal static async Task<PartitionMovePhaseResult> LocalAsync(D2ImportSession session,
        PartitionMovePhaseCommand phase, CancellationToken token, Guid? commandId = null)
    {
        var request = Request(session, phase, commandId ?? Guid.NewGuid(), null, null);
        var operation = await ControlledPartitionMovementProcessIssuer.IssueAsync(session.Source,
            session.SourceAdmission, request, token);
        var result = session.Source.Journal.Submit(operation, token);
        ControlledPartitionMovementProcessSetup.Require(result);
        return result.Get<PartitionMovePhaseResult>();
    }

    internal static async Task<(PartitionMovementTransportRequest Request, Guid GrantId)> AuthorizeAsync(
        D2ImportSession session, PartitionMovePhaseResult state, PartitionMovePhaseCommand effect,
        bool atSource, CancellationToken token, PartitionMovementTransportAction action = PartitionMovementTransportAction.Apply)
    {
        var id = Guid.NewGuid();
        var grantId = Guid.NewGuid();
        var receiver = atSource ? session.Owners.Control.Owner : session.Owners.Destination.Owner;
        var body = NativeSerialization.Serialize(new PartitionMoveAuthorizeBody(grantId, id, D2ImportCrashProtocol.Principal,
            effect, receiver, session.ExpiresAt));
        var authorized = await LocalAsync(session, Phase(session, state, PartitionMovePeerStage.ControlAuthorize, body), token, grantId);
        var grant = authorized.Grant ?? throw new InvalidOperationException("No original native grant was issued.");
        var request = Request(session, effect, id, grant, authorized.Journal) with
        {
            Action = action
        };
        return (request, grantId);
    }

    internal static async Task<(PartitionMovePhaseResult Result, ReplicatedOperation Original, Guid GrantId, OperationResult Outcome)> EffectAsync(
        D2ImportSession session, PartitionMovePhaseResult state, PartitionMovePhaseCommand effect,
        bool atSource, CancellationToken token)
    {
        var issued = await AuthorizeAsync(session, state, effect, atSource, token);
        var node = atSource ? session.Source : session.Target;
        var admission = atSource ? session.SourceAdmission : session.TargetAdmission;
        var original = await ControlledPartitionMovementProcessIssuer.IssueAsync(node, admission, issued.Request, token);
        var outcome = node.Journal.Submit(original, token);
        ControlledPartitionMovementProcessSetup.Require(outcome);
        var result = outcome.Get<PartitionMovePhaseResult>();
        await AcknowledgeAsync(session, state, issued.GrantId, result, token);
        return (result, original, issued.GrantId, outcome);
    }

    internal static Task<PartitionMovePhaseResult> AcknowledgeAsync(D2ImportSession session,
        PartitionMovePhaseResult state, Guid grantId, PartitionMovePhaseResult effect, CancellationToken token)
        => LocalAsync(session, Phase(session, state, PartitionMovePeerStage.ControlAcknowledge,
            NativeSerialization.Serialize(new PartitionMoveAcknowledgeBody(grantId, effect.Journal))), token);

    internal static PartitionMovementTransportRequest Request(D2ImportSession session, PartitionMovePhaseCommand phase,
        Guid commandId, PartitionMovePhaseGrant? grant, PartitionMoveJournalReceipt? authorization)
        => new(commandId, new(phase.Version, phase.MoveId, phase.Partition, phase.ControlOwner, phase.SourcePlacement,
            phase.DestinationOwner, phase.ControlIntentDigest, phase.Stage, phase.PageOrdinal, session.ExpiresAt,
            Guid.NewGuid(), phase.Body, grant), authorization, session.Owners.Control.Owner.VoterIds.First(),
            session.Caller, PartitionMovementTransportAction.Apply, Guid.Empty, D2ImportCrashProtocol.Empty);
}
