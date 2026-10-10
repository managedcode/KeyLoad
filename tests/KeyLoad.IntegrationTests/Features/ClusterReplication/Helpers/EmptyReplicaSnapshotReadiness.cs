using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Joins one original public status caller before the unchanged independent stopped snapshot oracle.</summary>
internal static class EmptyReplicaSnapshotReadiness
{
    private const long Threshold = 16;
    private const long TailCommands = 1;
    private const long NoSnapshot = 0;

    internal static Task ProvisionAsync(ClusterFixture fixture, CancellationToken token)
        => RequireOwner(fixture).ProvisionAsync(fixture, token);

    internal static async Task RequireAsync(ClusterFixture fixture, string node, NodeStatus installed,
        CommitReceipt final, CancellationToken token)
    {
        var owner = RequireOwner(fixture);
        var discovery = await Task.WhenAll(RequestCqrsProbeFixtureProtocol.Nodes.Select(name =>
            fixture.ReadSnapshotDiscoveryAsync(name, token)));
        var selected = discovery.Single(item => item.VoterId == RequestCqrsProbeFileNames.OriginForNode(node));
        var controls = owner.Controls;
        var arm = controls.WriteArm(ClusterFixtureSnapshotReadiness.PrincipalId, Guid.Empty, GrainReadKind.NodeStatus,
            RequestCqrsProbePhase.ReplicaSnapshotReadiness, RequestCqrsProbeAction.Hold, targetVoter: selected.VoterId);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var producer = fixture.Client(node, owner.Secret).StatusAsync(cancellation.Token);
        var observed = controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.ReplicaSnapshotReadiness,
            RequestCqrsProbeOutcome.Observed, discovery, cancellation.Token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => CompleteAsync(controls, node, installed, final,
            arm, producer, observed, discovery, cancellation.Token), failures);
        if (failures.Count != 0)
        {
            controls.RetainEvidence();
            ServerFailureObserver.Observe(cancellation.Cancel, failures);
            await ServerFailureObserver.ObserveAsync(async () => { _ = await producer; }, failures);
            await ServerFailureObserver.ObserveAsync(async () => { _ = await observed; }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CompleteAsync(RequestCqrsProbeFixture controls, string node,
        NodeStatus installed, CommitReceipt final, Guid arm, Task<Result<NodeStatus>> producer,
        Task<RequestCqrsProbeMarkerRecord> observed, IReadOnlyList<ReplicaSiloDiscovery> discovery, CancellationToken token)
    {
        // A failed original caller wakes this observation; no waiting on an impossible held marker.
        if (await Task.WhenAny(observed, producer) == producer)
        { _ = ClusterReplicationTestSupport.Success(await producer); }
        var marker = await observed;
        var native = ReadCut(controls, node, marker);
        await RequireCutAsync(native, final);
        controls.WriteRelease(arm, marker.RequestId);
        var current = ClusterReplicationTestSupport.Success(await producer);
        await controls.WaitForSettlementAsync(arm, discovery, token);
        _ = await controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.ProducerDisposed,
            RequestCqrsProbeOutcome.Observed, discovery, token);
        await Assert.That(current.NodeId).IsEqualTo(installed.NodeId);
        await Assert.That(current.Incarnation).IsEqualTo(installed.Incarnation);
        await Assert.That(current.Applied).IsEqualTo(native.MaterializedPosition);
        await Assert.That(current.ReadGeneration).IsGreaterThanOrEqualTo(installed.ReadGeneration);
        await controls.RetireArmAsync(arm, token);
    }

    private static RequestCqrsReplicaSnapshotRecord ReadCut(RequestCqrsProbeFixture controls,
        string node, RequestCqrsProbeMarkerRecord marker)
    {
        var owned = controls.NodeFor(node);
        RequestCqrsProbeFileStore.VerifyOwnerFile(owned.Directory, owned.OwnerBytes);
        _ = RequestCqrsProbeFileValidation.ValidateContents(owned.Directory);
        var identity = new RequestCqrsReplicaSnapshotRecord(RequestCqrsProbeProtocol.Version,
            RequestCqrsReplicaSnapshotJson.Kind, controls.SessionId, marker.ArmId, marker.RequestId,
            NoSnapshot, NoSnapshot, NoSnapshot, NoSnapshot);
        var value = RequestCqrsReplicaSnapshotJson.Read(controls.Json, RequestCqrsProbeFileStore.ReadRecord(
            Path.Combine(owned.Directory, RequestCqrsReplicaSnapshotJson.Name(identity))));
        if (value.SessionId != marker.SessionId || value.ArmId != marker.ArmId || value.RequestId != marker.RequestId)
        { throw RequestCqrsReplicaSnapshotJson.Invalid(); }
        return value;
    }

    private static async Task RequireCutAsync(RequestCqrsReplicaSnapshotRecord native, CommitReceipt final)
    {
        await Assert.That(native.SnapshotIndex).IsGreaterThan(NoSnapshot);
        await Assert.That(native.MaterializedPosition).IsGreaterThanOrEqualTo(final.Token.Position);
        await Assert.That(native.CommittedIndex).IsGreaterThanOrEqualTo(final.Token.Position);
        await Assert.That(native.LastIndex).IsGreaterThanOrEqualTo(native.CommittedIndex);
        await Assert.That(checked(native.LastIndex - native.SnapshotIndex + TailCommands)).IsLessThan(Threshold);
    }

    private static ClusterFixtureSnapshotReadiness RequireOwner(ClusterFixture fixture)
        => fixture.SnapshotReadiness ?? throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.InvalidArm);
}
