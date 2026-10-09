using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;
using static KeyLoad.IntegrationTests.Features.ClusterReplication.ClusterReplicationTestSupport;
using static KeyLoad.IntegrationTests.Features.ClusterReplication.RetainedReplicaSnapshotScenario;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class EmptyReplicaSnapshotScenario
{
    private const string Fault = "empty-follower-canonical-erase";
    private const string Collection = "snapshots";
    private const string TailDocument = "ordered-tail";
    private const string TailJson = "{\"tail\":true}";
    private const int NodeCount = 3;
    private const string OtherResource = "other-snapshot-resource";

    internal static async Task RunAsync(ClusterFixture fixture)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3), TimeProvider.System);
        StoppedReplica? stopped = null;
        var running = true;
        McpOfficialClient? mcp = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var state = await ConfigureAndProduceAsync(fixture, timeout.Token);
            var denied = await McpPersistedIdentity.CreateAsync(fixture, state.Partition, OtherResource,
                Capability.DocumentsRead, timeout.Token);
            var leader = new Uri(state.InitialStatuses[0].Leader!).Host;
            var index = Enumerable.Range(0, NodeCount).First(i => NodeName(i + 1) != leader);
            var node = NodeName(index + 1);
            stopped = new(index, node, Path.Combine(fixture.Root, node));
            await fixture.KillContainerAsync(node, Fault, timeout.Token);
            running = false;
            EmptyReplicaSnapshotStorage.Erase(fixture, node);
            var final = await ReplicateWhileStoppedAsync(fixture, state, index, timeout.Token);
            var survivor = state.Clients[(index + 1) % NodeCount];
            var tailCommand = new CommandRequest(Guid.NewGuid(), state.Partition,
                [new PutDocument(Collection, TailDocument, TailJson, ExpectedRevision: 0)]);
            var tail = Success(await RetryDuringElectionAsync(() => survivor.CommitAsync(tailCommand, timeout.Token), timeout.Token));
            var baseline = await EmptyReplicaSnapshotAssertions.CaptureAsync(survivor, state, timeout.Token);
            await fixture.RestartContainerAsync(node, timeout.Token);
            running = true;
            var installed = await RequireAppliedAsync(state.Clients[index], state.InitialStatuses[index], tail, timeout.Token);
            await Assert.That(installed.NodeId).IsNotEqualTo(state.InitialStatuses[index].NodeId);
            mcp = await McpOfficialClient.ConnectAsync(fixture, node, fixture.AdminKey, timeout.Token);
            await EmptyReplicaSnapshotAssertions.VerifyAsync(state.Clients[index], mcp, state, final, tailCommand, tail,
                baseline, timeout.Token);
            await EmptyReplicaSnapshotPolicyAssertions.DeniedThenHealthyAsync(fixture, node, denied.Secret,
                state.Partition, tail.Token, timeout.Token);
            var closing = mcp;
            mcp = null;
            await closing.DisposeAsync();
            await fixture.KillContainerAsync(node, Fault, timeout.Token);
            running = false;
            await RequireNativeCutAsync(fixture, node, tailCommand, tail);
            await fixture.RestartContainerAsync(node, timeout.Token);
            running = true;
            var reopened = await RequireAppliedAsync(state.Clients[index], installed, tail, timeout.Token);
            await Assert.That(reopened.NodeId).IsEqualTo(installed.NodeId);
            await EmptyReplicaSnapshotAssertions.HealthyAsync(fixture, node, state, tailCommand, tail, timeout.Token);
        }, failures);
        if (mcp is not null)
        { await ServerFailureObserver.ObserveAsync(() => mcp.DisposeAsync().AsTask(), failures); }
        if (!running && stopped is not null)
        {
            using var cleanup = new CancellationTokenSource(RequestCqrsRf3Protocol.CleanupDeadline, TimeProvider.System);
            await ServerFailureObserver.ObserveAsync(() => fixture.RestartContainerAsync(stopped.Name, cleanup.Token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RequireNativeCutAsync(ClusterFixture fixture, string node,
        CommandRequest tailCommand, CommitReceipt tail)
    {
        var actual = EmptyReplicaSnapshotStorage.ReadReplicaState(fixture, node, tail.Token.Position);
        var recovered = actual.State;
        await Assert.That(recovered.Incarnation).IsEqualTo(tail.Token.Incarnation);
        await Assert.That(recovered.Snapshot).IsNotNull();
        await Assert.That(recovered.Snapshot!.Index).IsGreaterThan(0L);
        await Assert.That(recovered.Snapshot.Index).IsLessThan(tail.Token.Position);
        EmptyReplicaSnapshotStorage.RequireImage(fixture, node, recovered.Snapshot);
        await Assert.That(recovered.CommittedIndex).IsGreaterThanOrEqualTo(tail.Token.Position);
        await Assert.That(recovered.LastIndex).IsGreaterThanOrEqualTo(recovered.CommittedIndex);
        await Assert.That(actual.Tail.Index).IsEqualTo(tail.Token.Position);
        await Assert.That(actual.Tail.Operation).IsNotNull();
        await Assert.That(actual.Tail.Operation!.Id).IsEqualTo(tailCommand.CommandId);
        await Assert.That(actual.Tail.Term).IsGreaterThanOrEqualTo(recovered.Snapshot.Term);
    }

    private static async Task<NodeStatus> RequireAppliedAsync(KeyLoadClient client, NodeStatus original, CommitReceipt tail,
        CancellationToken token)
    {
        await EventuallyAsync(async () =>
        {
            var result = await client.StatusAsync(token);
            return result.IsSuccess && result.Value!.RoutingReady && result.Value.Applied >= tail.Token.Position;
        }, token);
        var actual = Success(await client.StatusAsync(token));
        await Assert.That(Guid.TryParse(actual.NodeId, out var nativeOwner)).IsTrue();
        await Assert.That(nativeOwner).IsNotEqualTo(Guid.Empty);
        await Assert.That(actual.Incarnation).IsEqualTo(original.Incarnation);
        await Assert.That(actual.Voters).IsEqualTo(NodeCount);
        await Assert.That(actual.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(actual.ReadGeneration).IsGreaterThan(0L);
        return actual;
    }
}
