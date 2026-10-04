using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class AggregateReplayRf3LeaderLossTests(ClusterFixture fixture)
{
    [Test]
    public async Task AcEvent008SnapshotTailCommandRecoversAcrossLeaderLossAndContainerRestart()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await AggregateReplayRf3Scenario.CreateAsync(fixture, deadline.Token);
        var nodes = new[] { McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3 };
        using var clients = new WorkerClients(fixture, scenario.WorkerSecret, nodes);
        var append = await scenario.AppendInitialAsync(clients.Items[0], Guid.NewGuid(), deadline.Token);
        await Assert.That(append.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        var snapshot = await McpCallerAssertions.SdkSuccessAsync(await clients.Items[0].CommitAsync(
            scenario.InitialSnapshotCommand(Guid.NewGuid()), deadline.Token));
        await Assert.That(snapshot.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await RunLeaderLossAsync(scenario, nodes, clients.Items, clients.AdminItems, deadline.Token);
    }

    private async Task RunLeaderLossAsync(AggregateReplayRf3Scenario scenario, string[] nodes,
        KeyLoadClient[] clients, KeyLoadClient[] administrators, CancellationToken cancellationToken)
    {
        string? stoppedNode = null;
        var restarted = false;
        Exception? originalFailure = null;
        try
        {
            var leaderIndex = await FindLeaderIndexAsync(nodes, administrators, cancellationToken);
            stoppedNode = nodes[leaderIndex];
            await fixture.KillContainerAsync(stoppedNode, AggregateReplayRf3Tokens.FailureScenario, cancellationToken);
            var survivorIndex = Enumerable.Range(0, nodes.Length).First(index => index != leaderIndex);
            await WaitUntilReadyAsync(administrators[survivorIndex], cancellationToken);
            var frozen = scenario.FailoverCommand(Guid.NewGuid());
            var receipt = ClusterReplicationTestSupport.Success(await ClusterReplicationTestSupport.RetryDuringElectionAsync(
                () => clients[survivorIndex].CommitAsync(frozen, cancellationToken), cancellationToken));
            await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
            await Assert.That(receipt.Mutations.Length).IsEqualTo(2);
            var retry = ClusterReplicationTestSupport.Success(
                await clients[survivorIndex].CommitAsync(frozen, cancellationToken));
            await Assert.That(JsonDefaults.Serialize(retry).AsSpan()
                .SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();

            await fixture.RestartContainerAsync(stoppedNode, cancellationToken);
            restarted = true;
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(stoppedNode,
                WaitBehavior.WaitOnResourceUnavailable, cancellationToken);
            await WaitForClusterReadyAsync(nodes, administrators, cancellationToken);
            await Assert.That(Directory.Exists(Path.Combine(fixture.Root, stoppedNode))).IsTrue();
            await VerifyAllNodeParityAsync(scenario, nodes, clients, cancellationToken);
        }
        catch (Exception failure) when (ClusterReplicationTestSupport.IsNonFatalCleanupFailure(failure))
        {
            originalFailure = failure;
            await CaptureFailureDiagnosticsAsync(failure);
            throw;
        }
        finally
        {
            if (stoppedNode is not null && !restarted)
            {
                using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                await RestoreLeaderAsync(stoppedNode, nodes, administrators, originalFailure, recovery.Token);
            }
        }
    }

    private async Task CaptureFailureDiagnosticsAsync(Exception failure)
    {
        try
        {
            await fixture.SaveFailureDiagnosticsAsync();
        }
        catch (Exception diagnosticFailure) when (ClusterReplicationTestSupport.IsNonFatalCleanupFailure(diagnosticFailure))
        {
            failure.Data[AggregateReplayRf3Tokens.DiagnosticsFailureKey] = diagnosticFailure;
        }
    }

    private static async Task<int> FindLeaderIndexAsync(string[] nodes, KeyLoadClient[] clients,
        CancellationToken cancellationToken)
    {
        var statuses = (await Task.WhenAll(clients.Select(client => client.StatusAsync(cancellationToken))))
            .Select(ClusterReplicationTestSupport.Success).ToArray();
        var leader = new Uri(statuses[0].Leader
            ?? throw new InvalidOperationException(AggregateReplayRf3Tokens.MissingLeaderMessage));
        await Assert.That(statuses.All(status => status.Voters == AggregateReplayRf3Tokens.NodeCount)).IsTrue();
        await Assert.That(statuses.All(status => string.Equals(status.Leader, statuses[0].Leader,
            StringComparison.Ordinal))).IsTrue();
        return Enumerable.Range(0, clients.Length).Single(index => string.Equals(leader.Host,
            nodes[index], StringComparison.Ordinal));
    }

    private async Task VerifyAllNodeParityAsync(AggregateReplayRf3Scenario scenario, string[] nodes,
        KeyLoadClient[] clients, CancellationToken cancellationToken)
    {
        foreach (var index in Enumerable.Range(0, nodes.Length))
        {
            var request = scenario.ReadRequest(maximumEvents: AggregateReplayRf3Tokens.TailCount);
            var sdkPage = ClusterReplicationTestSupport.Success(
                await clients[index].ReadAggregateReplayAsync(request, cancellationToken));
            await using var mcp = await McpOfficialClient.ConnectAsync(fixture, nodes[index],
                scenario.WorkerSecret, cancellationToken);
            var mcpPage = await McpCallerAssertions.SuccessAsync<AggregateReplayPage>(await mcp.CallAsync(
                McpCallerTools.StreamsReplay, request, cancellationToken));
            await AggregateReplayRf3Assertions.SamePageAsync(sdkPage, mcpPage.Value,
                expectedCount: AggregateReplayRf3Tokens.FailoverReducedCount,
                expectedTailCount: AggregateReplayRf3Tokens.FailoverTailCount,
                expectedEventIds: [AggregateReplayRf3Tokens.DispatchedEventId,
                    AggregateReplayRf3Tokens.DeliveredEventId],
                expectedSnapshotState: AggregateReplayRf3Tokens.FailoverState,
                expectedSnapshotVersion: AggregateReplayRf3Tokens.SecondSnapshotVersion);
            await Assert.That(sdkPage.Snapshot!.SnapshotVersion)
                .IsEqualTo(AggregateReplayRf3Tokens.SecondSnapshotVersion);
        }
    }

    private async Task RestoreLeaderAsync(string stoppedNode, string[] nodes, KeyLoadClient[] administrators,
        Exception? originalFailure, CancellationToken cancellationToken)
    {
        try
        {
            await fixture.RestartContainerAsync(stoppedNode, cancellationToken);
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(stoppedNode,
                WaitBehavior.WaitOnResourceUnavailable, cancellationToken);
            await Assert.That(Directory.Exists(Path.Combine(fixture.Root, stoppedNode))).IsTrue();
            await WaitForClusterReadyAsync(nodes, administrators, cancellationToken);
        }
        catch (Exception recoveryFailure) when (ClusterReplicationTestSupport.IsNonFatalCleanupFailure(recoveryFailure))
        {
            if (originalFailure is not null)
            {
                originalFailure.Data[AggregateReplayRf3Tokens.RestartFailureKey] = recoveryFailure;
                return;
            }
            throw;
        }
    }

    private static async Task WaitUntilReadyAsync(KeyLoadClient client, CancellationToken cancellationToken)
        => await ClusterReplicationTestSupport.EventuallyAsync(async () =>
        {
            var status = await client.StatusAsync(cancellationToken);
            return status.IsSuccess && status.Value!.RoutingReady;
        }, cancellationToken);

    private static async Task WaitForClusterReadyAsync(string[] nodes, KeyLoadClient[] clients,
        CancellationToken cancellationToken)
    {
        foreach (var index in Enumerable.Range(0, nodes.Length))
        {
            await WaitUntilReadyAsync(clients[index], cancellationToken);
        }
    }

    private sealed class WorkerClients : IDisposable
    {
        internal KeyLoadClient[] Items { get; }
        internal KeyLoadClient[] AdminItems { get; }
        private readonly HttpClient[] clients;
        private readonly HttpClient[] adminClients;

        internal WorkerClients(ClusterFixture fixture, string secret, string[] nodes)
        {
            clients = nodes.Select(node => McpCallerHttp.Create(fixture, node)).ToArray();
            Items = clients.Select(client => new KeyLoadClient(client, secret)).ToArray();
            adminClients = nodes.Select(node => McpCallerHttp.Create(fixture, node)).ToArray();
            AdminItems = adminClients.Select(client => new KeyLoadClient(client, fixture.AdminKey)).ToArray();
        }

        public void Dispose()
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
            foreach (var client in adminClients)
            {
                client.Dispose();
            }
        }
    }
}
