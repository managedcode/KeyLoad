using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Consumes actual source admission and configured MAC receiver admission before each native phase.</summary>
internal sealed class ControlledDocumentNativePhaseFlow(ControlledPartitionMovementNode source,
    ControlledPartitionMovementNode target, ControlledPartitionMovementLoopbackCorpus corpus,
    ServerRuntimeOptions sourceOptions, ServerRuntimeOptions targetOptions,
    PartitionMovementPeerAdmission sourceAdmission, PartitionMovementPeerAdmission targetAdmission,
    string callerAddress, ReadExecutionBudget originalWork)
{
    private const int FirstOrdinal = 0;

    internal async Task<PartitionMovePhaseResult> ApplyAsync<T>(Guid commandId,
        PartitionControlDocumentCommandContext context, PartitionMovePeerStage stage, T body,
        DateTimeOffset expiry, PartitionMovePhaseGrant? grant,
        PartitionMoveJournalReceipt? authorization, CancellationToken token)
        => await ApplyEncodedAsync(commandId, context, stage, NativeSerialization.Serialize(body),
            expiry, grant, authorization, token);

    internal async Task<PartitionMovePhaseResult> ApplyEncodedAsync(Guid commandId,
        PartitionControlDocumentCommandContext context, PartitionMovePeerStage stage, ReadOnlyMemory<byte> body,
        DateTimeOffset expiry, PartitionMovePhaseGrant? grant,
        PartitionMoveJournalReceipt? authorization, CancellationToken token)
    {
        if (body.IsEmpty)
        { throw new InvalidOperationException(PartitionMoveProtocol.Invalid); }
        var envelope = new PartitionMovePeerEnvelope(context.Control.Version, context.Control.MoveId,
            context.Control.Partition, corpus.Control.Owner, context.Control.SourcePlacement,
            context.Control.DestinationOwner, PartitionMoveIntentIdentity.Digest(context.Control), stage,
            FirstOrdinal, expiry, Guid.NewGuid(), body, grant);
        source.Database.ValidatePartitionMovementDispatch(envelope, authorization, commandId, originalWork);
        var request = new PartitionMovementTransportRequest(commandId, envelope, authorization,
            corpus.Control.Owner.VoterIds[FirstOrdinal], callerAddress, PartitionMovementTransportAction.Apply,
            Guid.Empty, FirstOrdinal);
        var targetPhase = stage == PartitionMovePeerStage.ControlApplyCommand;
        var actual = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(targetPhase ? target : source,
            targetPhase ? targetOptions : sourceOptions, targetPhase ? targetAdmission : sourceAdmission,
            request, token);
        await Assert.That(actual.Error).IsNull();
        var result = actual.Get<PartitionMovePhaseResult>();
        await Assert.That(result.Journal.CommandId).IsEqualTo(commandId);
        await Assert.That(result.Stage).IsEqualTo(stage);
        await Assert.That(result.MoveId).IsEqualTo(context.Control.MoveId);
        originalWork.Check();
        return result;
    }

    internal PartitionMovePhaseCommand Propose<T>(PartitionControlDocumentCommandContext context,
        PartitionMovePeerStage stage, T body)
        => ProposeEncoded(context, stage, NativeSerialization.Serialize(body));

    internal PartitionMovePhaseCommand ProposeEncoded(PartitionControlDocumentCommandContext context,
        PartitionMovePeerStage stage, ReadOnlyMemory<byte> body)
        => new(context.Control.Version, context.Control.MoveId, context.Control.Partition,
            corpus.Control.Owner, context.Control.SourcePlacement, context.Control.DestinationOwner,
            PartitionMoveIntentIdentity.Digest(context.Control), stage, FirstOrdinal,
            body, Resources: []);
}
