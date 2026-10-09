using KeyLoad.Client;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>The actual original expiry cancels its sealed hold; it never stands in for an ordered tombstone denial.</summary>
internal sealed class PartitionMovementExpiredRetireSealedOperationRf3Trial
{
    private const string Administrator = "root";
    private readonly List<Exception> failures = [];
    private TwoRf3MembershipWave? wave;
    private PartitionMovementPublicParentRf3Seed? seed;
    private ReplicaSiloDiscovery[]? discovery;
    private Task<Result<PartitionMoveResult>>? producer;

    internal static async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var parent = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var trial = new PartitionMovementExpiredRetireSealedOperationRf3Trial();
        await ServerFailureObserver.ObserveAsync(() => trial.ExecuteAsync(parent.Token), trial.failures);
        using var cleanup = new CancellationTokenSource(TwoRf3MembershipProtocol.CleanupDeadline, TimeProvider.System);
        if (trial.wave is { } owned && trial.discovery is { } signed)
        { await ServerFailureObserver.ObserveAsync(() => owned.QueryControls.ReleaseOpenArmsAsync(signed, cleanup.Token), trial.failures); }
        if (trial.producer is { } operation)
        { await ServerFailureObserver.ObserveAsync(async () => { _ = await operation; }, trial.failures); }
        if (trial.seed is { } clients)
        { await ServerFailureObserver.ObserveAsync(() => clients.DisposeAsync().AsTask(), trial.failures); }
        if (trial.wave is { } resources)
        { await ServerFailureObserver.ObserveAsync(() => resources.DisposeAsync().AsTask(), trial.failures); }
        ServerFailureObserver.ThrowIfAny(trial.failures);
    }

    private async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        wave = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(cancellationToken);
        seed = await PartitionMovementPublicParentRf3Seed.CreateAsync(wave, cancellationToken);
        discovery = await ReadDiscoveryAsync(wave, cancellationToken);
        var originalId = PartitionMovementParentPhaseIds.For(seed.FirstRequest, Administrator,
            PartitionMovementParentPhaseRole.Retire);
        var arm = wave.QueryControls.WriteArm(Administrator, originalId, null,
            RequestCqrsProbePhase.RetireOperationSealed, RequestCqrsProbeAction.Hold);
        producer = seed.Source.MovePartitionAsync(seed.FirstRequest, cancellationToken);
        var sealedMarker = await wave.QueryControls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.RetireOperationSealed,
            RequestCqrsProbeOutcome.Observed, discovery, cancellationToken);
        await Assert.That(sealedMarker.CommandId).IsEqualTo(originalId);
        var cancelled = await wave.QueryControls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.RetireOperationSealed,
            RequestCqrsProbeOutcome.Cancelled, discovery, cancellationToken);
        await Assert.That(cancelled.RequestId).IsEqualTo(sealedMarker.RequestId);
        await Assert.That(cancelled.CommandId).IsEqualTo(originalId);
        await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(wave.QueryControls, arm, sealedMarker.RequestId,
            originalId, discovery, cancellationToken);
        await Assert.That((await producer).IsFailed).IsTrue();
        await wave.QueryControls.RetireArmAsync(arm, cancellationToken);
        var before = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, originalId, cancellationToken);
        var original = before.First(cut => cut.Pending?.OriginalPhaseCommandId == originalId).Pending!;
        await Assert.That(original.Stage).IsEqualTo(PartitionMovePeerStage.Retire);
        await Assert.That(original.OriginalExpiresAt <= TimeProvider.System.GetUtcNow()).IsTrue();
        await Assert.That(original.OriginalResult).IsNull();
        await Assert.That(original.RetireCancellationAttempt).IsNull();
        await Assert.That(before.All(cut => cut.OriginalNativeResult is null && cut.RetireCancellation is null)).IsTrue();
        await Assert.That(before.Count(cut => cut.OriginalReceiverIssuance is not null)).IsEqualTo(TwoRf3MembershipProtocol.MembersPerGroup);
        await PartitionMovementRetireGrantDispositionRf3Trial.RequirePendingAsync(before, original);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken);
        discovery = await ReadDiscoveryAsync(wave, cancellationToken);
        await seed.VerifyAsync(cancellationToken);
        var healthy = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.MovePartitionAsync(seed.FirstRequest, cancellationToken));
        await Assert.That(healthy.Phase).IsEqualTo(PartitionMovePhase.Retired);
        var terminal = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, originalId, cancellationToken);
        await PartitionMovementRetireGrantDispositionRf3Trial.RequireTerminalAsync(terminal, original);
        await Assert.That(terminal.First(cut => cut.Header is not null).Header!.CleanupGeneration).IsGreaterThan(original.CleanupGeneration);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken);
        await SqlRf3Protocol.EqualAsync(healthy, await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.MovePartitionAsync(seed.FirstRequest, cancellationToken)));
        await seed.VerifyAsync(cancellationToken);
    }

    internal static async Task<ReplicaSiloDiscovery[]> ReadDiscoveryAsync(TwoRf3MembershipWave owned,
        CancellationToken cancellationToken)
    {
        var result = new List<ReplicaSiloDiscovery>();
        foreach (var node in TwoRf3MembershipProtocol.Nodes.Take(TwoRf3MembershipProtocol.MembersPerGroup))
        { result.Add(await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(owned.Application, node, owned.Profile, cancellationToken)); }
        return result.ToArray();
    }
}
