using KeyLoad.Client;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Actual stopped native rows distinguish unknown cancellation from the one genuine disposal ACK.</summary>
internal static class PartitionMovementRetireGrantDispositionRf3Trial
{
    private const string Administrator = PartitionMovementPublicParentRf3Administrator.PrincipalId;
    private const string DatabaseDirectory = "database";

    internal static async Task<PartitionMoveResult> RunCorruptAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, PartitionMoveParentPhase original, CancellationToken cancellationToken)
    {
        var discovery = await PartitionMovementExpiredRetireSealedOperationRf3Trial.ReadDiscoveryAsync(wave, cancellationToken);
        var retainedId = PartitionMovementParentPhaseIds.For(seed.FirstRequest, Administrator, PartitionMovementParentPhaseRole.Retire,
            original.PageOrdinal, checked(original.CleanupGeneration + PartitionMoveProtocol.SequenceStep));
        var arm = wave.QueryControls.WriteArm(Administrator, retainedId, null, RequestCqrsProbePhase.RetireOperationSealed, RequestCqrsProbeAction.Hold);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var producer = seed.Source.MovePartitionAsync(seed.FirstRequest, caller.Token);
        var failures = new List<Exception>();
        PartitionMovementStoppedNativeSnapshot? snapshot = null;
        PartitionMoveResult? healthy = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var marker = await wave.QueryControls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.RetireOperationSealed,
                RequestCqrsProbeOutcome.Observed, discovery, cancellationToken);
            await caller.CancelAsync();
            await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(wave.QueryControls, arm, marker.RequestId,
                retainedId, discovery, cancellationToken);
            await Assert.That((await producer).IsFailed).IsTrue();
            await wave.QueryControls.RetireArmAsync(arm, cancellationToken);
            var originalSnapshot = await CaptureExpiredPendingAsync(wave, seed, original, retainedId, cancellationToken);
            snapshot = originalSnapshot.Snapshot;
            CorruptStoppedGrants(wave, originalSnapshot.Stopped);
            var corruptBefore = TwoRf3MembershipProtocol.Nodes.Select(node => PartitionMovementPublicParentRf3Cut.ReadStopped(
                wave, node, seed.FirstRequest, original.OriginalPhaseCommandId)).ToArray();
            await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken);
            var refused = await seed.Source.MovePartitionAsync(seed.FirstRequest, cancellationToken);
            await Assert.That(refused.IsFailed).IsTrue();
            await Assert.That(refused.Problem?.ErrorCode).IsEqualTo(ErrorCode.Corruption.ToString());
            var corruptAfter = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
                original.OriginalPhaseCommandId, cancellationToken);
            await SqlRf3Protocol.EqualAsync(corruptBefore, corruptAfter);
            // A startup failure above remains an initiating failure; it is not this validator oracle.
        }, failures);
        using var cleanup = new CancellationTokenSource(TwoRf3MembershipProtocol.CleanupDeadline, TimeProvider.System);
        await ServerFailureObserver.ObserveAsync(caller.CancelAsync, failures);
        await ServerFailureObserver.ObserveAsync(() => wave.QueryControls.ReleaseOpenArmsAsync(discovery, cleanup.Token), failures);
        await ServerFailureObserver.ObserveAsync(async () => { _ = await producer; }, failures);
        if (snapshot is { } owned)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var corrupt = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
                    original.OriginalPhaseCommandId, cleanup.Token);
                await owned.RestoreAsync(wave, corrupt, cleanup.Token);
                await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cleanup.Token);
                healthy = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.MovePartitionAsync(seed.FirstRequest, cleanup.Token));
                await Assert.That(healthy.Phase).IsEqualTo(PartitionMovePhase.Retired);
                await seed.VerifyAsync(cleanup.Token);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return healthy ?? throw new InvalidOperationException("The genuine restored parent result is absent.");
    }

    private static async Task<(PartitionMovementStoppedNativeSnapshot Snapshot, PartitionMovementPublicParentRf3NativeCut[] Stopped)> CaptureExpiredPendingAsync(
        TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed, PartitionMoveParentPhase original,
        Guid retainedId, CancellationToken cancellationToken)
    {
        var stopped = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            original.OriginalPhaseCommandId, cancellationToken);
        var pending = stopped.First(cut => cut.Pending?.OriginalPhaseCommandId == retainedId).Pending!;
        await Assert.That(pending.Stage).IsEqualTo(PartitionMovePeerStage.Retire);
        await Assert.That(pending.CleanupGeneration).IsEqualTo(checked(original.CleanupGeneration + PartitionMoveProtocol.SequenceStep));
        var pendingCuts = TwoRf3MembershipProtocol.Nodes.Select(node => PartitionMovementPublicParentRf3Cut.ReadStopped(
            wave, node, seed.FirstRequest, retainedId)).ToArray();
        await Assert.That(pendingCuts.All(cut => cut.OriginalNativeResult is null)).IsTrue();
        var remaining = pending.OriginalExpiresAt - TimeProvider.System.GetUtcNow();
        if (remaining > TimeSpan.Zero)
        { await Task.Delay(remaining, TimeProvider.System, cancellationToken); }
        var snapshot = await PartitionMovementStoppedNativeSnapshot.CaptureAsync(wave, stopped, cancellationToken);
        return (snapshot, stopped);
    }

    private static void CorruptStoppedGrants(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3NativeCut[] stopped)
    {
        foreach (var cut in stopped.Where(cut => cut.OriginalControlGrant is not null))
        {
            var grant = cut.OriginalControlGrant!;
            var disposition = grant.RetireCancellationDisposition
                ?? throw new InvalidOperationException("The genuine original grant cancellation disposition is absent.");
            var failures = new List<Exception>();
            ZoneTreeStore? store = null;
            ServerFailureObserver.Observe(() =>
            {
                store = new(new(Path.Combine(wave.OwnedDataRoot, cut.Node, DatabaseDirectory)),
                    IntegrationExecutionOptions.StorageExecution(), IntegrationExecutionOptions.PointCacheExecution());
                store.Commit((transaction, _) =>
                {
                    PartitionMoveGrantStorage.Write(transaction,
                        grant with { RetireCancellationDisposition = disposition with { OriginalNonce = Guid.NewGuid() } },
                        IntegrationExecutionOptions.DatabaseLimits().Value.MaxBatchBytes);
                    return true;
                });
            }, failures);
            if (store is { } owned)
            { ServerFailureObserver.Observe(owned.Dispose, failures); }
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }

    internal static async Task RequirePendingAsync(PartitionMovementPublicParentRf3NativeCut[] cuts,
        PartitionMoveParentPhase original)
    {
        var controls = cuts.Where(cut => cut.OriginalControlGrant is not null).ToArray();
        await Assert.That(controls.Length).IsEqualTo(TwoRf3MembershipProtocol.MembersPerGroup);
        foreach (var cut in controls)
        {
            await SqlRf3Protocol.EqualAsync(cut.OriginalControlGrant, original.OriginalGrant);
            await Assert.That(cut.OriginalControlGrant!.RetireCancellationDisposition).IsNull();
            await Assert.That(cut.OriginalControlGrant.Settlement).IsNull();
            await Assert.That(cut.OriginalControlGrant.AbortDisposition).IsNull();
            await Assert.That(cut.OutstandingMoveGrants).IsGreaterThan(PartitionMoveProtocol.EmptyCount);
            await Assert.That(cut.OutstandingPrincipalGrants).IsNotNull();
            await Assert.That(cut.OutstandingPrincipalGrants!.Value).IsGreaterThanOrEqualTo(cut.OutstandingMoveGrants);
            await Assert.That(cut.OutstandingDatabaseGrants).IsGreaterThanOrEqualTo(cut.OutstandingMoveGrants);
        }
    }

    internal static async Task RequireTerminalAsync(PartitionMovementPublicParentRf3NativeCut[] cuts,
        PartitionMoveParentPhase original)
    {
        var controls = cuts.Where(cut => cut.OriginalControlGrant is not null).ToArray();
        await Assert.That(controls.Length).IsEqualTo(TwoRf3MembershipProtocol.MembersPerGroup);
        foreach (var cut in controls)
        {
            var grant = cut.OriginalControlGrant!;
            var phase = cut.OriginalPhase!;
            await Assert.That(phase.RetireCancellation).IsNotNull();
            await Assert.That(phase.RetireCancellationAttempt).IsNotNull();
            await Assert.That(phase.ObservationCheckpointReceipt).IsNotNull();
            await Assert.That(phase.OriginalResult).IsNull();
            await Assert.That(grant.RetireCancellationDisposition).IsNotNull();
            await Assert.That(grant.Settlement).IsNull();
            await Assert.That(grant.AbortDisposition).IsNull();
            await SqlRf3Protocol.EqualAsync(grant with { RetireCancellationDisposition = null }, original.OriginalGrant);
            await SqlRf3Protocol.EqualAsync(phase.OriginalGrant, original.OriginalGrant);
            var disposal = grant.RetireCancellationDisposition!;
            var cancellation = phase.RetireCancellation!.Cancellation;
            await Assert.That(disposal.Version).IsEqualTo(PartitionMoveProtocol.Version);
            await Assert.That(disposal.OriginalPhaseCommandId).IsEqualTo(original.OriginalPhaseCommandId);
            await Assert.That(disposal.OriginalPhaseIdentityDigest).IsEqualTo(original.OriginalPhaseIdentityDigest);
            await Assert.That(disposal.OriginalNonce).IsEqualTo(original.OriginalRequestNonce);
            await Assert.That(disposal.OriginalExpiresAt).IsEqualTo(original.OriginalExpiresAt);
            await Assert.That(disposal.CleanupGeneration).IsEqualTo(original.CleanupGeneration);
            await Assert.That(disposal.CancellationCommandId).IsEqualTo(cancellation.CancellationCommandId);
            await Assert.That(disposal.CancellationCommandId).IsEqualTo(phase.RetireCancellationAttempt!.CancellationCommandId);
            await Assert.That(disposal.ReceiverPrincipalId).IsEqualTo(cancellation.CancellationPrincipalId);
            await Assert.That(disposal.ReceiverPolicyEpoch).IsEqualTo(cancellation.CancellationPolicyEpoch);
            await SqlRf3Protocol.EqualAsync(disposal.CancellationReceipt, cancellation.CancellationReceipt);
            await Assert.That(cut.OutstandingMoveGrants).IsEqualTo(PartitionMoveProtocol.EmptyCount);
            await Assert.That(cut.OutstandingPrincipalGrants).IsEqualTo((long?)PartitionMoveProtocol.EmptyCount);
            await Assert.That(cut.OutstandingDatabaseGrants).IsEqualTo(PartitionMoveProtocol.EmptyCount);
        }
    }
}
