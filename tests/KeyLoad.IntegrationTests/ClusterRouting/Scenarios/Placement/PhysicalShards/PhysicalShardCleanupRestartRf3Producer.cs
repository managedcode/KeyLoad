using KeyLoad.Client;
using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class PhysicalShardCleanupRestartRf3Producer
{
    private const int FirstEntry = 0;
    internal const int SecondEntry = 1;
    internal const int EmptyFailures = 0;
    private readonly List<Exception> failures = [];
    private RequestCqrsProbeMarkerRecord? marker;
    private RequestCqrsMovementJournalRecord? proof;

    internal static async Task RunAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed[] seeds, CancellationToken token)
    {
        var requests = seeds.Select(seed => seed.FirstRequest).ToArray();
        var phase = PartitionMovementParentPhaseIds.For(requests[FirstEntry],
            PartitionMovementPublicParentRf3Administrator.PrincipalId, PartitionMovementParentPhaseRole.Retire);
        var discovery = await PhysicalShardPartitionMovementRf3FaultOwner.DiscoveryAsync(wave, token);
        var arm = wave.QueryControls.WriteArm(PartitionMovementPublicParentRf3Administrator.PrincipalId, phase, null,
            RequestCqrsProbePhase.SubmitReturned, RequestCqrsProbeAction.Hold, seeds[FirstEntry].Partition);
        var producer = new PhysicalShardCleanupRestartRf3Producer();
        await producer.CaptureAsync(wave, seeds, requests, phase, arm, discovery, token);
        var retained = producer.proof ?? throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.MarkerMismatch);
        await PhysicalShardCleanupRestartRf3Assertions.ColdAsync(wave, seeds, retained,
            wave.QueryControls.ArmFor(arm).GateVoter!);
        await PhysicalShardNonRetiredPlanRf3Resume.RunAsync(wave, seeds, requests, token);
        await wave.QueryControls.RetireArmAsync(arm, token);
    }

    private async Task CaptureAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed[] seeds, PartitionMoveRequest[] requests, Guid phase, Guid arm,
        IReadOnlyList<ReplicaSiloDiscovery> discovery, CancellationToken token)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var originalOwner = wave.Application;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var iterator = seeds[FirstEntry].Source.MoveWholePartitionsAsync(requests,
                cancellation.Token).GetAsyncEnumerator(CancellationToken.None);
            var first = iterator.MoveNextAsync().AsTask();
            await ServerFailureObserver.ObserveAsync(() => ObserveKillAsync(wave, seeds[FirstEntry], phase,
                arm, discovery, first, token), failures);
            if (proof is { } observed && marker is { } held && wave.applicationDisposed && wave.nodeLocksReleased)
            {
                await ServerFailureObserver.ObserveAsync(() => first, failures);
                await ServerFailureObserver.ObserveAsync(() => TerminalAsync(iterator, first), failures);
                ServerFailureObserver.Observe(() => wave.QueryControls.JoinKilledOwnerGates(wave,
                    originalOwner, [arm], observed, held, first), failures);
            }
            else
            {
                await ServerFailureObserver.ObserveAsync(cancellation.CancelAsync, failures);
                await ServerFailureObserver.ObserveAsync(() => wave.QueryControls.ReleaseOpenArmsAsync(discovery, token), failures);
                await ServerFailureObserver.ObserveAsync(() => first, failures);
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task ObserveKillAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        Guid phase, Guid arm, IReadOnlyList<ReplicaSiloDiscovery> discovery, Task first, CancellationToken token)
    {
        marker = await wave.QueryControls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.SubmitReturned,
            RequestCqrsProbeOutcome.Observed, discovery, token);
        proof = RequestCqrsMovementJournalReader.Read(wave.QueryControls, marker.Value);
        await PhysicalShardCleanupRestartRf3Assertions.ProofAsync(proof.Value, seed, phase);
        await Assert.That(first.IsCompleted).IsFalse();
        var node = wave.QueryControls.ArmFor(arm).GateVoter
            ?? throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.MarkerMismatch);
        await PhysicalShardPartitionMovementRf3FaultOwner.KillAndJoinAsync(wave, node, token);
    }

    private static async Task TerminalAsync(IAsyncEnumerator<ManagedCode.Communication.Result<PartitionMoveResult>> iterator,
        Task<bool> first)
    {
        await Assert.That(first.IsCompletedSuccessfully).IsTrue();
        await Assert.That(await first.ConfigureAwait(false)).IsTrue();
        await Assert.That(iterator.Current.IsSuccess).IsFalse();
        await Assert.That(iterator.Current.Value).IsNull();
        await Assert.That(iterator.Current.Problem).IsNotNull();
        await Assert.That(await iterator.MoveNextAsync()).IsFalse();
    }
}
