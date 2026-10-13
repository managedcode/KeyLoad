using KeyLoad.Client;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardPartitionMovementRf3FaultFlow
{
    private const int FirstEntry = 0;

    internal static async Task RunAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed[] seeds, CancellationToken token)
    {
        var requests = seeds.Select(seed => seed.FirstRequest).ToArray();
        var controls = wave.QueryControls;
        var discovery = await PhysicalShardPartitionMovementRf3FaultOwner.DiscoveryAsync(wave, token);
        var phase = PartitionMovementParentPhaseIds.For(requests[FirstEntry],
            PartitionMovementPublicParentRf3Administrator.PrincipalId, PartitionMovementParentPhaseRole.Prepare);
        var arm = controls.WriteArm(PartitionMovementPublicParentRf3Administrator.PrincipalId, phase, null,
            RequestCqrsProbePhase.SubmitReturned, RequestCqrsProbeAction.Hold, seeds[FirstEntry].Partition);
        var originalOwner = wave.Application;
        var failures = new List<Exception>();
        RequestCqrsProbeMarkerRecord? marker = null;
        RequestCqrsMovementJournalRecord? proof = null;
        using var originalCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            await using var iterator = seeds[FirstEntry].Source.MoveWholePartitionsAsync(requests,
                originalCancellation.Token).GetAsyncEnumerator(CancellationToken.None);
            var first = iterator.MoveNextAsync().AsTask();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                marker = await controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.SubmitReturned,
                    RequestCqrsProbeOutcome.Observed, discovery, token);
                proof = RequestCqrsMovementJournalReader.Read(controls, marker.Value);
                await PhysicalShardPartitionMovementRf3JournalAssertions.ProofAsync(proof.Value, seeds[FirstEntry], phase);
                await Assert.That(first.IsCompleted).IsFalse();
                var node = controls.ArmFor(arm).GateVoter
                    ?? throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.MarkerMismatch);
                await PhysicalShardPartitionMovementRf3FaultOwner.KillAndJoinAsync(wave, node, token);
            }, failures);
            if (proof is { } observed && marker is { } held && wave.applicationDisposed && wave.nodeLocksReleased)
            {
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    await Assert.That(await first).IsTrue();
                    var original = iterator.Current;
                    await Assert.That(original.IsSuccess).IsFalse();
                    await Assert.That(original.Value).IsNull();
                    await Assert.That(original.Problem).IsNotNull();
                    await Assert.That(await iterator.MoveNextAsync()).IsFalse();
                }, failures);
                ServerFailureObserver.Observe(() => controls.JoinKilledOwnerGates(wave, originalOwner,
                    [arm], observed, held, first), failures);
            }
            else
            {
                await CleanupLiveAsync(controls, arm, first, originalCancellation, discovery, failures, token);
            }
        }
        catch (Exception cleanup) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(cleanup) is null)
        { failures.Add(cleanup); }
        catch (Exception cleanup) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(cleanup) is not null)
        { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
        var retained = proof ?? throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.MarkerMismatch);
        await PhysicalShardPartitionMovementRf3JournalAssertions.ColdAsync(wave, seeds, retained,
            controls.ArmFor(arm).GateVoter!);
        await PhysicalShardPartitionMovementRf3FaultOwner.ResumeAsync(wave, seeds, requests, token);
        await controls.RetireArmAsync(arm, token);
    }

    private static async Task CleanupLiveAsync(RequestCqrsProbeFixture controls, Guid arm, Task original,
        CancellationTokenSource originalCancellation, IReadOnlyList<ReplicaSiloDiscovery> discovery,
        List<Exception> failures, CancellationToken token)
    {
        await ServerFailureObserver.ObserveAsync(originalCancellation.CancelAsync, failures);
        await ServerFailureObserver.ObserveAsync(async () => await original, failures);
        var state = controls.ArmFor(arm);
        if (state.RequestId is null)
        { return; }
        await ServerFailureObserver.ObserveAsync(() => controls.ReleaseOpenArmsAsync(discovery, token), failures);
        await ServerFailureObserver.ObserveAsync(async () => await controls.WaitForMarkerAsync(arm,
            RequestCqrsProbePhase.ProducerDisposed, RequestCqrsProbeOutcome.Observed, discovery, token), failures);
        await ServerFailureObserver.ObserveAsync(() => controls.RetireArmAsync(arm, token), failures);
    }
}
