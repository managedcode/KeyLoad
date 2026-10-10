using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ChangeFeeds;
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
    private const long NativeSnapshotThreshold = 16;
    private const long TailCommandsBeforeNativeInspection = 1;
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
            var (index, node, originalFeed) = await CaptureFollowerFeedAsync(state, timeout.Token);
            stopped = new(index, node, Path.Combine(fixture.Root, node));
            await fixture.KillContainerAsync(node, Fault, timeout.Token);
            running = false;
            EmptyReplicaSnapshotStorage.Erase(fixture, node);
            var final = await ReplicateWhileStoppedAsync(fixture, state, index, timeout.Token);
            await fixture.RestartContainerAsync(node, timeout.Token);
            running = true;
            var installed = await RequireAppliedAsync(state.Clients[index], state.InitialStatuses[index], final.Receipt, timeout.Token);
            await Assert.That(installed.NodeId).IsNotEqualTo(state.InitialStatuses[index].NodeId);
            await fixture.KillContainerAsync(node, Fault, timeout.Token);
            running = false;
            var installedSnapshot = await RequireInstalledCutAsync(fixture, node, final.Receipt);
            await fixture.RestartContainerAsync(node, timeout.Token);
            running = true;
            var (tailCommand, tail, baseline) = await CreatePostInstallTailAsync(state, index, installed,
                final.Receipt, timeout.Token);
            await fixture.KillContainerAsync(node, Fault, timeout.Token);
            running = false;
            await RequireNativeCutAsync(fixture, node, tailCommand, tail, installedSnapshot);
            await fixture.RestartContainerAsync(node, timeout.Token);
            running = true;
            var reopened = await RequireAppliedAsync(state.Clients[index], installed, tail, timeout.Token);
            await Assert.That(reopened.NodeId).IsEqualTo(installed.NodeId);
            mcp = await McpOfficialClient.ConnectAsync(fixture, node, fixture.AdminKey, timeout.Token);
            await SnapshotInstallFeedAssertions.RequireAsync(state.Clients[index], mcp,
                state.Partition, originalFeed, final.Receipt, tail, timeout.Token);
            await EmptyReplicaSnapshotAssertions.VerifyAsync(state.Clients[index], mcp, state, final, tailCommand, tail,
                baseline, timeout.Token);
            await EmptyReplicaSnapshotPolicyAssertions.DeniedThenHealthyAsync(fixture, node, denied.Secret,
                state.Partition, tail.Token, timeout.Token);
            var closing = mcp;
            mcp = null;
            await closing.DisposeAsync();
            mcp = await McpOfficialClient.ConnectAsync(fixture, node, fixture.AdminKey, timeout.Token);
            await EmptyReplicaSnapshotAssertions.VerifyAsync(state.Clients[index], mcp, state, final, tailCommand, tail,
                baseline, timeout.Token, allRoutes: true);
            closing = mcp;
            mcp = null;
            await closing.DisposeAsync();
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

    private static async Task<(int Index, string Node, ChangeFeedPage Feed)> CaptureFollowerFeedAsync(
        SnapshotState state, CancellationToken token)
    {
        var leader = new Uri(state.InitialStatuses[0].Leader!).Host;
        var index = Enumerable.Range(0, NodeCount).First(i => NodeName(i + 1) != leader);
        var node = NodeName(index + 1);
        var originalFeed = await SnapshotInstallFeedAssertions.CaptureAsync(state.Clients[index], state.Partition, token);
        return (index, node, originalFeed);
    }

    private static async Task<long> RequireInstalledCutAsync(ClusterFixture fixture, string node, CommitReceipt final)
    {
        var actual = EmptyReplicaSnapshotStorage.ReadInstalledState(fixture, node);
        await Assert.That(actual.Incarnation).IsEqualTo(final.Token.Incarnation);
        await Assert.That(actual.Snapshot).IsNotNull();
        await Assert.That(actual.Snapshot!.Index).IsGreaterThan(0L);
        await Assert.That(actual.CommittedIndex).IsGreaterThanOrEqualTo(final.Token.Position);
        await Assert.That(actual.LastIndex).IsGreaterThanOrEqualTo(actual.CommittedIndex);
        await Assert.That(checked(actual.LastIndex - actual.Snapshot.Index + TailCommandsBeforeNativeInspection)).IsLessThan(NativeSnapshotThreshold);
        EmptyReplicaSnapshotStorage.RequireImage(fixture, node, actual.Snapshot);
        return actual.Snapshot.Index;
    }

    private static async Task RequireNativeCutAsync(ClusterFixture fixture, string node,
        CommandRequest tailCommand, CommitReceipt tail, long installedSnapshot)
    {
        var actual = EmptyReplicaSnapshotStorage.ReadReplicaState(fixture, node, tail.Token.Position, installedSnapshot);
        var recovered = actual.State;
        try
        {
            await Assert.That(recovered.Incarnation).IsEqualTo(tail.Token.Incarnation);
            await Assert.That(recovered.Snapshot).IsNotNull();
            await Assert.That(recovered.Snapshot!.Index).IsGreaterThan(0L);
            await Assert.That(recovered.Snapshot.Index).IsGreaterThanOrEqualTo(installedSnapshot);
            await Assert.That(recovered.Snapshot.Index).IsLessThan(tail.Token.Position);
            await Assert.That(checked(recovered.LastIndex - recovered.Snapshot.Index)).IsLessThan(NativeSnapshotThreshold);
            EmptyReplicaSnapshotStorage.RequireImage(fixture, node, recovered.Snapshot);
            await Assert.That(recovered.CommittedIndex).IsGreaterThanOrEqualTo(tail.Token.Position);
            await Assert.That(recovered.LastIndex).IsGreaterThanOrEqualTo(recovered.CommittedIndex);
            await Assert.That(actual.Tail.Index).IsEqualTo(tail.Token.Position);
            await Assert.That(actual.Tail.Operation).IsNotNull();
            await Assert.That(actual.Tail.Operation!.Id).IsEqualTo(tailCommand.CommandId);
            await Assert.That(actual.Tail.Term).IsGreaterThanOrEqualTo(recovered.Snapshot.Term);
        }
        catch (Exception failure) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            EmptyReplicaSnapshotStorage.RetainCutFailure(failure, installedSnapshot, recovered, tail.Token.Position, hasEntry: true);
            throw;
        }
        catch (Exception failure) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            EmptyReplicaSnapshotStorage.RetainCutFailure(failure, installedSnapshot, recovered, tail.Token.Position, hasEntry: true);
            throw;
        }
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
    private static async Task<(CommandRequest Command, CommitReceipt Receipt, EmptyReplicaSnapshotBaseline Baseline)>
        CreatePostInstallTailAsync(SnapshotState state, int index, NodeStatus installed,
            CommitReceipt finalReceipt, CancellationToken token)
    {
        var beforeTail = await RequireAppliedAsync(state.Clients[index], installed, finalReceipt, token);
        await Assert.That(beforeTail.NodeId).IsEqualTo(installed.NodeId);
        await Assert.That(beforeTail.ReadGeneration).IsGreaterThanOrEqualTo(installed.ReadGeneration);
        var tailCommand = new CommandRequest(Guid.NewGuid(), state.Partition,
            [new PutDocument(Collection, TailDocument, TailJson, ExpectedRevision: 0)]);
        var tail = Success(await RetryDuringElectionAsync(() => state.Clients[(index + 1) % NodeCount].CommitAsync(tailCommand, token), token));
        var baseline = await EmptyReplicaSnapshotAssertions.CaptureAsync(state.Clients[(index + 1) % NodeCount], state, token);
        var afterTail = await RequireAppliedAsync(state.Clients[index], beforeTail, tail, token);
        await Assert.That(afterTail.NodeId).IsEqualTo(installed.NodeId);
        await Assert.That(afterTail.ReadGeneration).IsGreaterThanOrEqualTo(beforeTail.ReadGeneration);
        return (tailCommand, tail, baseline);
    }

}
