using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Exercises real acknowledged parent cancellation, true cold reopen and exact late native Prepare denial.</summary>
internal static class PartitionMovementParentCancellationTrial
{
    private const int PeerKeyBytes = 32;

    internal static async Task ExecuteAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        ControlledPartitionMovementLoopbackListeners listeners, ControlledPartitionMovementLoopbackCorpus corpus)
        => await ExecuteCoreAsync(source, target, listeners, corpus, restorePolicy: false);

    internal static Task ExecutePolicyRestoreAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        ControlledPartitionMovementLoopbackListeners listeners, ControlledPartitionMovementLoopbackCorpus corpus)
        => ExecuteCoreAsync(source, target, listeners, corpus, restorePolicy: true);

    private static async Task ExecuteCoreAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        ControlledPartitionMovementLoopbackListeners listeners, ControlledPartitionMovementLoopbackCorpus corpus, bool restorePolicy)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var seeded = ControlledPartitionMovementSetup.SeedConfigured(source, target, corpus, out _, token);
        await Assert.That(seeded.Error).IsNull();
        var runtime = ControlledPartitionMovementLoopbackOptions.Bind(source, corpus,
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(PeerKeyBytes)),
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(PeerKeyBytes)), control: true);
        source.ReopenWithCheckpointVerifier(new PartitionMovementCheckpointVerifier(runtime.Node,
            runtime.ReplicaConfiguration, runtime.GrainRouting));
        var fixture = new PartitionMovementParentNativeFixture(source, corpus, runtime,
            ControlledPartitionMovementPrepareRequest.CallerAddress(listeners));
        var request = new PartitionMoveRequest(Guid.NewGuid(), ControlledPartitionMovementCorpus.Partition,
            corpus.Destination.Owner.PhysicalShardId, 0, PartitionMoveMode.Transfer);
        var initial = fixture.Read(request);
        var prepare = PartitionMovementParentPrepare.Create(PhysicalShardCatalogFixture.RootPrincipalId, request, initial);
        var phaseId = PartitionMovementParentPhaseIds.For(request, PhysicalShardCatalogFixture.RootPrincipalId,
            PartitionMovementParentPhaseRole.Prepare);
        var expiry = fixture.OriginalExpiry;
        var admitted = await fixture.CheckpointAsync(initial, PartitionMovementParentPhaseAdmission.Create(
            PhysicalShardCatalogFixture.RootPrincipalId, request, initial, phaseId, prepare, null, expiry), prepare);
        await Assert.That(admitted.Error).IsNull();
        var pending = fixture.Read(request, phaseId);
        await Assert.That(pending.Control).IsNull();
        await Assert.That(pending.Pending!.OriginalResult).IsNull();
        var abort = request with { Mode = PartitionMoveMode.Abort };
        var body = new PartitionMoveCheckpointBody(PartitionMoveProtocol.Version,
            PartitionMoveCheckpointAction.CancelUnprepared, PhysicalShardCatalogFixture.RootPrincipalId,
            abort, pending.Header!.Generation, phaseId, prepare, null, null, null, null, OriginalExpiresAt: expiry);
        var before = source.Store.Position;
        var sourceImage = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var targetImage = ControlledPartitionMovementRawImage.Bytes(target.Store);
        var rejected = await Assert.ThrowsAsync<KeyLoadException>(() => fixture.CheckpointAsync(pending,
            body with { OriginalExpiresAt = expiry.AddTicks(1) }, prepare));
        await Assert.That(rejected!.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(source.Store.Position).IsEqualTo(before);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store).SequenceEqual(sourceImage)).IsTrue();
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(target.Store).SequenceEqual(targetImage)).IsTrue();
        var cancelled = await fixture.CheckpointAsync(pending, body, prepare);
        await Assert.That(cancelled.Error).IsNull();
        var ownReceipt = cancelled.Get<PartitionMovePhaseResult>().Journal;
        if (restorePolicy)
        { await PartitionMovementParentPolicyRestoreTrial.ExecuteAsync(source, target, fixture, abort, phaseId); }
        var cold = await RequireColdCancellationAsync(source, fixture, abort, phaseId, prepare, ownReceipt);
        before = source.Store.Position;
        sourceImage = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var late = await Assert.ThrowsAsync<KeyLoadException>(() => fixture.OriginalAsync(phaseId, prepare, expiry));
        await Assert.That(late!.Code).IsEqualTo(restorePolicy ? ErrorCode.OwnershipLost : ErrorCode.Conflict);
        await Assert.That(source.Store.Position).IsEqualTo(before);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store).SequenceEqual(sourceImage)).IsTrue();
        var observed = body with
        {
            Action = PartitionMoveCheckpointAction.ObserveCancellation,
            ExpectedGeneration = cold.Header!.Generation,
            ObservedOriginalResult = cold.CancellationOutcome
        };
        var settled = await fixture.CheckpointAsync(cold, observed, prepare);
        await Assert.That(settled.Error).IsNull();
        await RequireTerminalAndHealthyAsync(source, target, fixture, request, phaseId, ownReceipt, targetImage);
    }

    private static async Task RequireTerminalAndHealthyAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, PartitionMovementParentNativeFixture fixture,
        PartitionMoveRequest request, Guid phaseId, PartitionMoveJournalReceipt ownReceipt, string[] targetImage)
    {
        source.Reopen();
        var terminal = fixture.Read(request with { Mode = PartitionMoveMode.Abort }, phaseId);
        await Assert.That(terminal.Header!.TerminalResult!.Phase).IsEqualTo(PartitionMovePhase.Aborted);
        await Assert.That(terminal.Pending).IsNull();
        await Assert.That(terminal.Interrupted!.OriginalResult).IsNull();
        await Assert.That(NativeSerialization.Serialize(terminal.Interrupted!.Cancellation!.CancellationReceipt)
            .SequenceEqual(NativeSerialization.Serialize(ownReceipt))).IsTrue();
        await Assert.That(source.Store.Read(view => PartitionMoveParentStorage.Counter(view,
            PartitionMoveParentKeys.DatabaseActive(request.Partition)))).IsEqualTo(0L);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(target.Store).SequenceEqual(targetImage)).IsTrue();
        await HealthyPrepareAsync(fixture, request with { MoveId = Guid.NewGuid() });
    }

    private static async Task<PartitionMoveParentState> RequireColdCancellationAsync(ControlledPartitionMovementNode source,
        PartitionMovementParentNativeFixture fixture, PartitionMoveRequest abort, Guid phaseId,
        PartitionMovePhaseCommand prepare, PartitionMoveJournalReceipt ownReceipt)
    {
        source.Reopen();
        var cold = fixture.Read(abort, phaseId);
        await Assert.That(cold.Header!.TerminalResult).IsNull();
        await Assert.That(cold.Header.PendingOriginalPhaseCommandId).IsEqualTo(phaseId);
        await Assert.That(cold.Pending!.OriginalResult).IsNull();
        await Assert.That(cold.Pending.OriginalIssuancePolicyEpoch).IsEqualTo(cold.Header.InitialPolicyEpoch);
        await Assert.That(cold.CancellationOutcome).IsNotNull();
        await Assert.That(NativeSerialization.Serialize(cold.Pending.Cancellation!.CancellationReceipt)
            .SequenceEqual(NativeSerialization.Serialize(ownReceipt))).IsTrue();
        await Assert.That(NativeSerialization.Serialize(cold.Pending.OriginalPhase!).SequenceEqual(NativeSerialization.Serialize(prepare))).IsTrue();
        await Assert.That(source.Store.Read(view => PartitionMoveParentStorage.Counter(view,
            PartitionMoveParentKeys.DatabaseActive(abort.Partition)))).IsEqualTo(1L);
        return cold;
    }

    private static async Task HealthyPrepareAsync(PartitionMovementParentNativeFixture fixture, PartitionMoveRequest request)
    {
        var state = fixture.Read(request);
        var prepare = PartitionMovementParentPrepare.Create(PhysicalShardCatalogFixture.RootPrincipalId, request, state);
        var id = PartitionMovementParentPhaseIds.For(request, PhysicalShardCatalogFixture.RootPrincipalId,
            PartitionMovementParentPhaseRole.Prepare);
        var expiry = fixture.OriginalExpiry;
        var admitted = await fixture.CheckpointAsync(state, PartitionMovementParentPhaseAdmission.Create(
            PhysicalShardCatalogFixture.RootPrincipalId, request, state, id, prepare, null, expiry), prepare);
        await Assert.That(admitted.Error).IsNull();
        var executed = await fixture.OriginalAsync(id, prepare, expiry);
        await Assert.That(executed.Error).IsNull();
        await Assert.That(executed.Get<PartitionMovePhaseResult>().Control!.Phase).IsEqualTo(PartitionMovePhase.Prepared);
    }
}
