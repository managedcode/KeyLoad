using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class ResourceLifetimeGrantStages
{
    internal const string UpdatedJson = "{\"state\":\"lifetime-protected\",\"text\":\"Київ knowledge\"}";
    internal const long InitialRevision = 1;
    internal const long UpdatedRevision = 2;
    private const int Ordinal = 0;
    private const long SequenceStep = 1;
    internal static readonly Guid OriginalId = Guid.Parse("f03c329c-b154-4fcb-b3b1-cd9816c8aa72");

    internal static async Task<ResourceLifetimeGrantState> AdmitAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ControlledPartitionMovementLoopbackListeners listeners,
        ControlledPartitionMovementLoopbackCorpus corpus, ServerRuntimeOptions sourceOptions,
        ServerRuntimeOptions targetOptions, PartitionMovementPeerAdmission sourceAdmission,
        PartitionMovementPeerAdmission targetAdmission, CancellationToken token)
    {
        var placement = source.Database.ReadAtomicPartitionPlacement("root",
            new(AtomicPartitionPlacementProtocol.CurrentVersion, ControlledPartitionMovementCorpus.Partition));
        var request = new CommandRequest(OriginalId, ControlledPartitionMovementCorpus.Partition,
            [new PutDocument(ControlledPartitionMovementCorpus.Collection, ControlledPartitionMovementCorpus.DocumentId,
                UpdatedJson, InitialRevision, ExplicitReplacement: true)], placement.PlacementEpoch);
        var original = source.Database.CreateNativeOperation(OperationKind.Batch, OriginalId, "root",
            source.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        var work = new ReadExecutionBudget(Options.Create(source.Database.Limits), source.Database.EvaluationClock, token);
        var context = source.Database.TryCaptureControlledDocumentCommand("root", original, work)
            ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        var expiry = source.Database.EvaluationClock.GetUtcNow() + sourceOptions.GrainRouting.Value.RequestLifetime;
        var phases = new ControlledDocumentNativePhaseFlow(source, target, corpus, sourceOptions, targetOptions,
            sourceAdmission, targetAdmission, ControlledPartitionMovementPrepareRequest.CallerAddress(listeners), work);
        var effectId = ControlledDocumentTechnicalIdentity.Derive(original, ControlledDocumentTechnicalIdentity.Effect);
        var admitted = await phases.ApplyAsync(ControlledDocumentTechnicalIdentity.Derive(original,
            ControlledDocumentTechnicalIdentity.Admit), context, PartitionMovePeerStage.ControlAdmitCommand,
            new PartitionControlAdmitBody(context.OperatorPrincipalId, context.Control, original, effectId, expiry),
            expiry, null, null, token);
        var record = admitted.ControlledCommand ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        return await AuthorizeAsync(source, target, corpus, targetOptions, targetAdmission, phases,
            original, context, record, expiry, ControlledPartitionMovementPrepareRequest.CallerAddress(listeners), token);
    }

    private static async Task<ResourceLifetimeGrantState> AuthorizeAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ControlledPartitionMovementLoopbackCorpus corpus,
        ServerRuntimeOptions targetOptions, PartitionMovementPeerAdmission admission, ControlledDocumentNativePhaseFlow phases,
        ReplicatedOperation original, PartitionControlDocumentCommandContext context, PartitionControlCommandRecord record,
        DateTimeOffset expiry, string caller, CancellationToken token)
    {
        var delegation = record.Delegation ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        var grantId = ControlledDocumentTechnicalIdentity.Derive(original, ControlledDocumentTechnicalIdentity.Authorize);
        var authorized = await phases.ApplyAsync(grantId, context, PartitionMovePeerStage.ControlAuthorize,
            new PartitionMoveAuthorizeBody(grantId, record.EffectId, context.OperatorPrincipalId,
                phases.ProposeEncoded(context, PartitionMovePeerStage.ControlApplyCommand, record.TargetBody),
                context.Control.DestinationOwner, delegation.ExpiresAt), expiry, null, null, token);
        var grant = authorized.Grant ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        var envelope = new PartitionMovePeerEnvelope(context.Control.Version, context.Control.MoveId,
            context.Control.Partition, corpus.Control.Owner, context.Control.SourcePlacement,
            context.Control.DestinationOwner, PartitionMoveIntentIdentity.Digest(context.Control),
            PartitionMovePeerStage.ControlApplyCommand, Ordinal, delegation.ExpiresAt, Guid.NewGuid(), record.TargetBody, grant);
        source.Database.ValidatePartitionMovementDispatch(envelope, authorized.Journal, record.EffectId,
            new ReadExecutionBudget(Options.Create(source.Database.Limits), source.Database.EvaluationClock, token));
        var proof = source.Database.CaptureControlledCommandLifetimeProof(envelope, token);
        var request = new PartitionMovementTransportRequest(record.EffectId, envelope, authorized.Journal,
            corpus.Control.Owner.VoterIds[Ordinal], caller, PartitionMovementTransportAction.Apply, Guid.Empty, Ordinal);
        return new(source, target, targetOptions, admission, phases, original, context, expiry, record,
            authorized, request, proof, target.Journal.Log.State.LastIndex);
    }

    internal static async Task<PartitionControlEffectPayload> CompleteAsync(ResourceLifetimeGrantState state, CancellationToken token)
    {
        var applied = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(state.Target, state.TargetOptions,
            state.TargetAdmission, state.Request, state.Proof, token);
        await Assert.That(applied.Error).IsNull();
        var phase = applied.Get<PartitionMovePhaseResult>();
        var effect = phase.ControlledEffect ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        var before = ControlledPartitionMovementExpiryOwnerState.Capture(state.Target);
        var replayRequest = state.Request with
        {
            Envelope = state.Request.Envelope with
            {
                Nonce = Guid.NewGuid()
            }
        };
        var replay = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(state.Target, state.TargetOptions,
            state.TargetAdmission, replayRequest, state.Proof, token);
        await ResourceLifetimeGrantAssertions.EqualAsync(applied, replay);
        await ResourceLifetimeGrantAssertions.EqualAsync(effect.OriginalResult,
            replay.Get<PartitionMovePhaseResult>().ControlledEffect!.OriginalResult);
        await before.AssertUnchangedAsync(state.Target);
        var grantId = state.Proof.OriginalGrant.GrantId;
        await state.Phases.ApplyAsync(ControlledDocumentTechnicalIdentity.Derive(state.Original,
            ControlledDocumentTechnicalIdentity.GrantAcknowledgement), state.Context, PartitionMovePeerStage.ControlAcknowledge,
            new PartitionMoveAcknowledgeBody(grantId, phase.Journal), state.Expiry, null, null, token);
        await ResourceLifetimeGrantAssertions.CommandPinnedAsync(state);
        await MetadataFenceAsync(state, token);
        await state.Phases.ApplyAsync(ControlledDocumentTechnicalIdentity.Derive(state.Original,
            ControlledDocumentTechnicalIdentity.CommandAcknowledgement), state.Context, PartitionMovePeerStage.ControlAcknowledgeCommand,
            new PartitionControlAcknowledgeBody(state.Context.OperatorPrincipalId, state.Record.Identity,
                state.Record.EffectId, effect.Receipt, effect.OriginalResult, grantId), state.Expiry, null, null, token);
        var finalized = await state.Phases.ApplyAsync(ControlledDocumentTechnicalIdentity.Derive(state.Original,
            ControlledDocumentTechnicalIdentity.Finalize), state.Context, PartitionMovePeerStage.ControlFinalizeCommand,
            new PartitionControlFinalizeBody(state.Context.OperatorPrincipalId, state.Record.Identity, state.Record.EffectId),
            state.Expiry, null, null, token);
        await Assert.That(finalized.ControlledCommand!.Phase).IsEqualTo(PartitionControlCommandPhase.Finalized);
        await ResourceLifetimeGrantAssertions.EqualAsync(state.Proof,
            state.Source.Database.CaptureControlledCommandLifetimeProof(state.Request.Envelope, token));
        return effect;
    }
    internal static async Task MetadataFenceAsync(ResourceLifetimeGrantState state, CancellationToken token)
    {
        var definition = state.Record.Delegation!.Resources.Single(value => value.Name == ControlledPartitionMovementCorpus.Collection);
        var partition = state.Context.Control.Partition;
        var request = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition);
        var operation = state.Source.Database.CreateNativeOperation(OperationKind.ConfigureResource, Guid.NewGuid(), "root",
            state.Source.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        var before = ControlledPartitionMovementExpiryOwnerState.Capture(state.Source);
        var target = ControlledPartitionMovementExpiryOwnerState.Capture(state.Target);
        var expected = new OperationResult(null, ErrorCode.Conflict, PartitionMoveProtocol.PolicyBusy);
        var actual = state.Source.Journal.Submit(operation, token);
        await ResourceLifetimeGrantAssertions.EqualAsync(expected, actual);
        var allowed = new[] { KeySpace.AppliedBytes, KeySpace.ClockBytes, KeySpace.GlobalOutcome(operation.PrincipalId, operation.Id) }
            .Select(key => Convert.ToHexString(key) + ":").ToArray();
        var after = ControlledPartitionMovementRawImage.Bytes(state.Source.Store);
        await Assert.That(after.Where(row => !allowed.Any(prefix => row.StartsWith(prefix, StringComparison.Ordinal)))
            .SequenceEqual(before.Image.Where(row => !allowed.Any(prefix => row.StartsWith(prefix, StringComparison.Ordinal))),
                StringComparer.Ordinal)).IsTrue();
        await Assert.That(state.Source.Store.Position).IsEqualTo(checked(before.Position + SequenceStep));
        await Assert.That(state.Source.Journal.Log.State.LastIndex).IsEqualTo(checked(before.LastIndex + SequenceStep));
        await Assert.That(state.Source.Journal.Log.State.CommittedIndex).IsEqualTo(checked(before.CommittedIndex + SequenceStep));
        await Assert.That(state.Source.Database.LastApplied).IsEqualTo(checked(before.LastIndex + SequenceStep));
        await target.AssertUnchangedAsync(state.Target);
        state.MetadataAttempts.Add((operation, actual));
    }

}
