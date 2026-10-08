using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>Queries actual terminal A and B phase outcomes after a genuine public protected replacement.</summary>
internal static class ProtectedDocumentRf3CommandOutcome
{
    private const string Administrator = "root";

    internal static async Task ExecuteAsync(TwoRf3MembershipWave wave, KeyLoadClient source, KeyLoadClient target,
        ProtectedDocumentRf3Seed seed, CommandRequest command, CommitReceipt originalReceipt, CancellationToken token,
        string originalSubject = Administrator)
    {
        var identityOnly = new ReplicatedOperation(command.CommandId, OperationKind.Batch, originalSubject,
            default, JsonSerializer.Serialize(command, JsonDefaults.Options));
        var effectId = ControlledDocumentTechnicalIdentity.Derive(identityOnly, ControlledDocumentTechnicalIdentity.Effect);
        var finalId = ControlledDocumentTechnicalIdentity.Derive(identityOnly, ControlledDocumentTechnicalIdentity.Finalize);
        var identity = new PartitionControlCommandIdentity(CommandOutcomeScopeKind.Partition, command.Partition,
            originalSubject, command.CommandId);
        var finalBody = new PartitionControlFinalizeBody(Administrator, identity, effectId);
        var finalQuery = Proposal(seed, finalId, PartitionMovePeerStage.ControlFinalizeCommand,
            NativeSerialization.Serialize(finalBody));
        var endpoint = new ProtectedDocumentRf3OutcomePeer(wave, seed.Peer);
        var beforeA = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var beforeB = await McpCallerAssertions.SdkSuccessAsync(await target.StatusAsync(token));
        _ = await endpoint.QueryAsync(finalQuery, false, token, wrongPath: true).ConfigureAwait(false);
        await UnchangedAsync(source, target, beforeA.Applied, beforeB.Applied, command, originalReceipt, token);
        _ = await endpoint.QueryAsync(finalQuery, false, token, badMac: true).ConfigureAwait(false);
        await UnchangedAsync(source, target, beforeA.Applied, beforeB.Applied, command, originalReceipt, token);
        var denied = finalQuery with
        {
            Envelope = finalQuery.Envelope with
            { Body = NativeSerialization.Serialize(finalBody with { OperatorPrincipalId = seed.DeniedId }) }
        };
        _ = await endpoint.QueryAsync(denied, false, token, expectedError: ErrorCode.PermissionDenied).ConfigureAwait(false);
        await UnchangedAsync(source, target, beforeA.Applied, beforeB.Applied, command, originalReceipt, token);
        var finalizedWitness = await endpoint.QueryAsync(finalQuery, false, token).ConfigureAwait(false)
            ?? throw new InvalidDataException(PartitionMovementProtocol.InvalidProof);
        var finalized = finalizedWitness.Result.Get<PartitionMovePhaseResult>();
        await Assert.That(finalized.Journal.CommandId).IsEqualTo(finalId);
        await Assert.That(finalized.Stage).IsEqualTo(PartitionMovePeerStage.ControlFinalizeCommand);
        var record = await OriginalAsync(finalized, identityOnly, identity, effectId, originalReceipt);
        var storedOriginal = record.OriginalOperation ?? throw new InvalidDataException(PartitionMovementProtocol.InvalidProof);
        var targetEffect = record.TargetEffect ?? throw new InvalidDataException(PartitionMovementProtocol.InvalidProof);
        var delegated = record.Delegation ?? throw new InvalidDataException(PartitionMovementProtocol.InvalidProof);
        if (record.TargetBody.IsEmpty)
        { throw new InvalidDataException(PartitionMovementProtocol.InvalidProof); }
        var authorized = await AuthorizationAsync(endpoint, seed, storedOriginal, record, delegated, token);
        var targetQuery = Proposal(seed, effectId, PartitionMovePeerStage.ControlApplyCommand, record.TargetBody,
            authorized.Grant!, authorized.Journal, delegated.ExpiresAt);
        var targetWitness = await endpoint.QueryAsync(targetQuery, true, token).ConfigureAwait(false)
            ?? throw new InvalidDataException(PartitionMovementProtocol.InvalidProof);
        var applied = targetWitness.Result.Get<PartitionMovePhaseResult>();
        await Assert.That(applied.Journal.CommandId).IsEqualTo(effectId);
        await Assert.That(applied.Stage).IsEqualTo(PartitionMovePeerStage.ControlApplyCommand);
        await Assert.That(applied.Journal.AppliedPosition).IsEqualTo(originalReceipt.Token.Position);
        await Assert.That(PhysicalOwnerEntryValidation.SameOwner(applied.Journal.PhysicalOwner, seed.Peer.Target)).IsTrue();
        await EqualAsync(applied.ControlledEffect!.Receipt, targetEffect);
        await EqualAsync(applied.ControlledEffect.OriginalResult.Get<CommitReceipt>(), originalReceipt);
        var nextFinal = await endpoint.QueryAsync(finalQuery, false, token).ConfigureAwait(false)
            ?? throw new InvalidDataException(PartitionMovementProtocol.InvalidProof);
        await EqualAsync(nextFinal.Result.Get<PartitionMovePhaseResult>(), finalized);
        await UnchangedAsync(source, target, beforeA.Applied, beforeB.Applied, command, originalReceipt, token);
    }

    private static async Task<PartitionControlCommandRecord> OriginalAsync(PartitionMovePhaseResult finalized,
        ReplicatedOperation original, PartitionControlCommandIdentity identity, Guid effectId, CommitReceipt receipt)
    {
        var record = finalized.ControlledCommand ?? throw new InvalidDataException(PartitionMovementProtocol.InvalidProof);
        await Assert.That(record.Phase).IsEqualTo(PartitionControlCommandPhase.Finalized);
        await Assert.That(record.Identity).IsEqualTo(identity);
        await Assert.That(record.EffectId).IsEqualTo(effectId);
        var stored = record.OriginalOperation ?? throw new InvalidDataException(PartitionMovementProtocol.InvalidProof);
        await Assert.That(stored.Id).IsEqualTo(original.Id);
        await Assert.That(stored.Kind).IsEqualTo(OperationKind.Batch);
        await Assert.That(stored.PrincipalId).IsEqualTo(original.PrincipalId);
        await Assert.That(stored.PayloadJson).IsEqualTo(original.PayloadJson);
        await EqualAsync(record.OriginalResult!.Get<CommitReceipt>(), receipt);
        var target = record.TargetEffect ?? throw new InvalidDataException(PartitionMovementProtocol.InvalidProof);
        await Assert.That(target.CommandId).IsEqualTo(effectId);
        await Assert.That(target.CommandId).IsNotEqualTo(original.Id);
        await EqualAsync(target with { CommandId = original.Id }, receipt);
        return record;
    }

    private static async Task<PartitionMovePhaseResult> AuthorizationAsync(ProtectedDocumentRf3OutcomePeer endpoint,
        ProtectedDocumentRf3Seed seed, ReplicatedOperation original, PartitionControlCommandRecord record,
        PartitionControlDelegation delegation, CancellationToken token)
    {
        var grantId = ControlledDocumentTechnicalIdentity.Derive(original, ControlledDocumentTechnicalIdentity.Authorize);
        var control = seed.Control;
        var proposed = new PartitionMovePhaseCommand(control.Version, control.MoveId, control.Partition,
            seed.Peer.Source, control.SourcePlacement, control.DestinationOwner, PartitionMoveIntentIdentity.Digest(control),
            PartitionMovePeerStage.ControlApplyCommand, 0, record.TargetBody, Resources: []);
        var body = new PartitionMoveAuthorizeBody(grantId, record.EffectId, Administrator, proposed,
            control.DestinationOwner, delegation.ExpiresAt);
        var request = Proposal(seed, grantId, PartitionMovePeerStage.ControlAuthorize, NativeSerialization.Serialize(body));
        var witness = await endpoint.QueryAsync(request, false, token).ConfigureAwait(false)
            ?? throw new InvalidDataException(PartitionMovementProtocol.InvalidProof);
        var result = witness.Result.Get<PartitionMovePhaseResult>();
        await Assert.That(result.Journal.CommandId).IsEqualTo(grantId);
        await Assert.That(result.Stage).IsEqualTo(PartitionMovePeerStage.ControlAuthorize);
        await Assert.That(result.Grant!.PhaseCommandId).IsEqualTo(record.EffectId);
        await Assert.That(result.Grant.GrantId).IsEqualTo(grantId);
        return result;
    }

    private static PartitionMovementTransportRequest Proposal(ProtectedDocumentRf3Seed seed, Guid id,
        PartitionMovePeerStage stage, ReadOnlyMemory<byte> body, PartitionMovePhaseGrant? grant = null,
        PartitionMoveJournalReceipt? authorization = null, DateTimeOffset? expiry = null)
    {
        var control = seed.Control;
        var envelope = new PartitionMovePeerEnvelope(control.Version, control.MoveId, control.Partition,
            seed.Peer.Source, control.SourcePlacement, control.DestinationOwner, PartitionMoveIntentIdentity.Digest(control),
            stage, 0, expiry ?? seed.Peer.Timing.OutcomeExpiry, Guid.NewGuid(), body, grant);
        return seed.Peer.Proposal(id, envelope, authorization);
    }

    private static async Task UnchangedAsync(KeyLoadClient source, KeyLoadClient target, long sourceIndex,
        long targetIndex, CommandRequest command, CommitReceipt expected, CancellationToken token)
    {
        await EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await source.CommitAsync(command, token)), expected);
        await Assert.That((await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token))).Applied).IsGreaterThanOrEqualTo(sourceIndex);
        await Assert.That((await McpCallerAssertions.SdkSuccessAsync(await target.StatusAsync(token))).Applied).IsGreaterThanOrEqualTo(targetIndex);
    }

    private static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
