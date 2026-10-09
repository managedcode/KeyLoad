using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Proves genuine native Prepare wins the ordered cancellation race and cold observation retains its exact body.</summary>
internal static class PartitionMovementParentPreparedWinnerTrial
{
    private const int PeerKeyBytes = 32;

    internal static async Task ExecuteAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        ControlledPartitionMovementLoopbackListeners listeners, ControlledPartitionMovementLoopbackCorpus corpus)
    {
        var seeded = ControlledPartitionMovementSetup.SeedConfigured(source, target, corpus, out _,
            TestContext.Current!.Execution.CancellationToken);
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
        await PartitionMovementParentOmissionTrial.BeforePrepareAsync(source, target, fixture,
            request, phaseId, prepare, expiry);
        var executed = await fixture.OriginalAsync(phaseId, prepare, expiry);
        await Assert.That(executed.Error).IsNull();
        await Assert.That(executed.Get<PartitionMovePhaseResult>().Control!.Phase).IsEqualTo(PartitionMovePhase.Prepared);
        source.Reopen();
        var pending = fixture.Read(request, phaseId);
        await DeniedCancellationAsync(source, target, fixture, request, pending, prepare, phaseId, expiry);
        var observed = new PartitionMoveCheckpointBody(PartitionMoveProtocol.Version,
            PartitionMoveCheckpointAction.Observe, PhysicalShardCatalogFixture.RootPrincipalId, request,
            pending.Header!.Generation, phaseId, null, null, executed, null, null, OriginalExpiresAt: expiry);
        var result = await fixture.CheckpointAsync(pending, observed, prepare);
        await Assert.That(result.Error).IsNull();
        source.Reopen();
        var cold = fixture.Read(request, phaseId);
        await Assert.That(cold.Pending).IsNull();
        await Assert.That(cold.Selected!.ObservationCheckpointReceipt).IsNotNull();
        await Assert.That(cold.Selected.OriginalExpiresAt).IsEqualTo(expiry);
        await Assert.That(NativeSerialization.Serialize(cold.Selected.OriginalPhase)
            .SequenceEqual(NativeSerialization.Serialize(prepare))).IsTrue();
        await Assert.That(NativeSerialization.Serialize(cold.Selected.OriginalResult)
            .SequenceEqual(NativeSerialization.Serialize(executed))).IsTrue();
        await PartitionMovementParentOmissionTrial.AfterPrepareAsync(source, target, fixture,
            request, phaseId, prepare, expiry);
        var before = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var position = source.Store.Position;
        var index = source.Journal.Log.State.LastIndex;
        var replay = await fixture.OriginalAsync(phaseId, prepare, expiry);
        await Assert.That(replay.Error).IsNull();
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(executed))).IsTrue();
        await Assert.That(source.Store.Position).IsEqualTo(position);
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(index);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store).SequenceEqual(before)).IsTrue();
    }

    private static async Task DeniedCancellationAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, PartitionMovementParentNativeFixture fixture,
        PartitionMoveRequest request, PartitionMoveParentState pending, PartitionMovePhaseCommand prepare,
        Guid phaseId, DateTimeOffset expiry)
    {
        var sourceImage = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var targetImage = ControlledPartitionMovementRawImage.Bytes(target.Store);
        var sourcePosition = source.Store.Position;
        var targetPosition = target.Store.Position;
        var last = source.Journal.Log.State.LastIndex;
        var committed = source.Journal.Log.State.CommittedIndex;
        var applied = source.Database.LastApplied;
        var body = new PartitionMoveCheckpointBody(PartitionMoveProtocol.Version,
            PartitionMoveCheckpointAction.CancelUnprepared, PhysicalShardCatalogFixture.RootPrincipalId,
            request with { Mode = PartitionMoveMode.Abort }, pending.Header!.Generation, phaseId,
            prepare, null, null, null, null, OriginalExpiresAt: expiry);
        var denied = await Assert.ThrowsAsync<KeyLoadException>(() => fixture.CheckpointAsync(pending, body, prepare));
        await Assert.That(denied!.Code).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(source.Store.Position).IsEqualTo(sourcePosition);
        await Assert.That(target.Store.Position).IsEqualTo(targetPosition);
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(last);
        await Assert.That(source.Journal.Log.State.CommittedIndex).IsEqualTo(committed);
        await Assert.That(source.Database.LastApplied).IsEqualTo(applied);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store).SequenceEqual(sourceImage)).IsTrue();
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(target.Store).SequenceEqual(targetImage)).IsTrue();
    }
}
