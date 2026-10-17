using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Supporting native denial controls; genuine expired Retire settlement is qualified separately through RF3.</summary>
internal static class PartitionMovementExpiredRetireCancellationTrial
{
    private const int PeerKeyBytes = 32;

    internal static Task CallerAuthorityAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        ControlledPartitionMovementLoopbackListeners listeners, ControlledPartitionMovementLoopbackCorpus corpus)
        => ExecuteAsync(source, target, listeners, corpus, callerAuthority: true);

    internal static Task PrepareScopeAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        ControlledPartitionMovementLoopbackListeners listeners, ControlledPartitionMovementLoopbackCorpus corpus)
        => ExecuteAsync(source, target, listeners, corpus, callerAuthority: false);

    private static async Task ExecuteAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        ControlledPartitionMovementLoopbackListeners listeners, ControlledPartitionMovementLoopbackCorpus corpus, bool callerAuthority)
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
            corpus.Destination.Owner.PhysicalShardId, PartitionMoveProtocol.EmptyCount, PartitionMoveMode.Transfer);
        var initial = fixture.Read(request);
        var prepare = PartitionMovementParentPrepare.Create(PhysicalShardCatalogFixture.RootPrincipalId, request, initial);
        var phaseId = PartitionMovementParentPhaseIds.For(request, PhysicalShardCatalogFixture.RootPrincipalId,
            PartitionMovementParentPhaseRole.Prepare);
        var expiry = fixture.OriginalExpiry;
        var admission = await fixture.CheckpointAsync(initial, PartitionMovementParentPhaseAdmission.Create(
            PhysicalShardCatalogFixture.RootPrincipalId, request, initial, phaseId, prepare, null, expiry), prepare);
        await Assert.That(admission.Error).IsNull();
        var pending = fixture.Read(request, phaseId).Pending!;
        var envelope = PartitionMovementParentCheckpointOwner.OriginalEnvelope(pending, release: false);
        var cancellation = new PartitionMoveRetireCancellationBody(PartitionMoveProtocol.Version, Guid.NewGuid(),
            phaseId, envelope, admission.Get<PartitionMovePhaseResult>().Journal, PartitionMoveProtocol.EmptyCount,
            ReadOnlyMemory<byte>.Empty, string.Empty, expiry,
            ActualReceiverPrincipalId: callerAuthority ? PhysicalShardCatalogFixture.RootPrincipalId : null,
            ActualReceiverPolicyEpoch: callerAuthority ? pending.OriginalIssuancePolicyEpoch : PartitionMoveProtocol.EmptyCount);
        var cut = source.Store.Position;
        var sourceBytes = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var targetBytes = ControlledPartitionMovementRawImage.Bytes(target.Store);
        var work = new ReadExecutionBudget(runtime.Core.DatabaseLimits, source.Database.EvaluationClock, token);
        var rejected = await Assert.ThrowsAsync<KeyLoadException>(() => Task.FromResult(
            source.Database.CreateVerifiedPartitionMovementRetireCancellation(PhysicalShardCatalogFixture.RootPrincipalId,
                cancellation, work)));
        await Assert.That(rejected!.Code).IsEqualTo(callerAuthority ? ErrorCode.PermissionDenied : ErrorCode.OwnershipLost);
        await Assert.That(source.Store.Position).IsEqualTo(cut);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store).SequenceEqual(sourceBytes)).IsTrue();
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(target.Store).SequenceEqual(targetBytes)).IsTrue();
        source.Reopen();
        var cold = fixture.Read(request, phaseId);
        await Assert.That(cold.Pending!.OriginalResult).IsNull();
        await Assert.That(cold.Pending.RetireCancellation).IsNull();
        await Assert.That(cold.Pending.RetireCancellationAttempt).IsNull();
        await Assert.That(NativeSerialization.Serialize(cold.Pending.OriginalPhase).SequenceEqual(NativeSerialization.Serialize(prepare))).IsTrue();
        var healthy = await fixture.OriginalAsync(phaseId, prepare, expiry);
        await Assert.That(healthy.Error).IsNull();
        await Assert.That(healthy.Get<PartitionMovePhaseResult>().Control!.Phase).IsEqualTo(PartitionMovePhase.Prepared);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(target.Store).SequenceEqual(targetBytes)).IsTrue();
    }
}
