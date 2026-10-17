using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.Search;

/// <summary>AC-GSEARCH-006: committed graph writes remain readable across a scoped RF3 leader loss.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class GraphSearchRf3LeaderLossTests(ClusterFixture fixture)
{
    private const string RestartFailureKey = "graphSearchRestartFailure";
    private const string DiagnosticsFailureKey = "graphSearchDiagnosticsFailure";
    private static readonly string[] Nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];

    [Test]
    public async Task AcGsearch006CommittedGraphMutationSurvivesLeaderLossAndRestart()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await GraphSearchRf3Scenario.CreateAsync(fixture, deadline.Token);
        var identity = await scenario.CreateReaderAsync(fixture, deadline.Token);
        using var clients = new GraphSearchRf3NodeClients(fixture, identity.Secret, Nodes);
        await RunLossAndRecoveryAsync(scenario, clients, deadline.Token);
    }

    private async Task RunLossAndRecoveryAsync(GraphSearchRf3Scenario scenario,
        GraphSearchRf3NodeClients clients, CancellationToken cancellationToken)
    {
        string? stoppedNode = null;
        var restarted = false;
        Exception? primary = null;
        var addition = GraphSearchRf3DurableAddition.Create(scenario);
        try
        {
            var leaderIndex = await FindLeaderIndexAsync(clients.Administrators, cancellationToken);
            stoppedNode = Nodes[leaderIndex];
            await fixture.KillContainerAsync(stoppedNode, "graph-search-rf3-leader-loss", cancellationToken);
            var survivor = await GraphSearchRf3SurvivorReadiness.WaitAsync(scenario,
                clients.Administrators, Nodes, leaderIndex, cancellationToken);
            var receipt = await WriteAndReadOnSurvivorAsync(scenario, clients, survivor, addition, cancellationToken);
            await fixture.RestartContainerAsync(stoppedNode, cancellationToken);
            restarted = true;
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(stoppedNode,
                WaitBehavior.WaitOnResourceUnavailable, cancellationToken);
            await VerifyClusterCatchupAsync(scenario, clients, addition, receipt, cancellationToken);
        }
        catch (Exception failure) when (ClusterReplicationTestSupport.IsNonFatalCleanupFailure(failure))
        {
            primary = failure;
            await CaptureDiagnosticsAsync(failure);
            throw;
        }
        finally
        {
            if (stoppedNode is not null && !restarted)
            {
                await RestoreNodeAsync(stoppedNode, clients.Administrators, primary);
            }
        }
    }

    private async Task<CommitReceipt> WriteAndReadOnSurvivorAsync(GraphSearchRf3Scenario scenario,
        GraphSearchRf3NodeClients clients, int survivor, CommandRequest addition, CancellationToken cancellationToken)
    {
        var original = await McpCallerAssertions.SdkSuccessAsync(await clients.Readers[survivor]
            .GraphSearchAsync(GraphSearchRf3Scenario.Request(scenario.Partition), cancellationToken));
        await GraphSearchRf3LeaderLossLiterals.HitsAsync(scenario, original, added: false);
        var receipt = await GraphSearchRf3DurableAddition.CommitAsync(clients.Administrators[survivor], addition, cancellationToken);
        await GraphSearchRf3DurableAddition.ReplayAsync(clients.Administrators[survivor], addition, receipt, cancellationToken);
        await using (var adminMcp = await McpOfficialClient.ConnectAsync(fixture, Nodes[survivor], fixture.AdminKey, cancellationToken))
        {
            var replay = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await adminMcp.CallAsync(
                McpCallerTools.DocumentsCommit, addition, cancellationToken));
            await Assert.That(JsonDefaults.Serialize(replay.Value).AsSpan().SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
        }
        var request = GraphSearchRf3Scenario.Request(scenario.Partition);
        var result = await McpCallerAssertions.SdkSuccessAsync(await clients.Readers[survivor]
            .GraphSearchAsync(request, cancellationToken));
        await GraphSearchRf3Assertions.AssertHitsAsync(result,
            (GraphSearchRf3Scenario.Alpha, 1d / 61), (GraphSearchRf3Scenario.Beta, 1d / 62),
            ("delta", 1d / 63), (GraphSearchRf3Scenario.Gamma, 1d / 64));
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, Nodes[survivor],
            clients.ReaderSecret, cancellationToken);
        var official = await McpCallerAssertions.SuccessAsync<GraphSearchResult>(await mcp.CallAsync(
            GraphSearchRf3Scenario.SearchGraphTool, request, cancellationToken));
        await GraphSearchRf3Assertions.AssertEquivalentAsync(result, official.Value);
        await GraphSearchRf3LeaderLossLiterals.HitsAsync(scenario, result, added: true);
        await GraphSearchRf3LeaderLossLiterals.HitsAsync(scenario, official.Value, added: true);
        await GraphSearchRf3LeaderLossLiterals.GraphAsync(scenario, clients.Administrators[survivor], cancellationToken);
        return receipt;
    }

    private static async Task<int> FindLeaderIndexAsync(KeyLoadClient[] administrators,
        CancellationToken cancellationToken)
    {
        var statuses = (await Task.WhenAll(administrators.Select(client => client.StatusAsync(cancellationToken))))
            .Select(ClusterReplicationTestSupport.Success).ToArray();
        await Assert.That(statuses.All(status => status.Voters == Nodes.Length)).IsTrue();
        await Assert.That(statuses.All(status => string.Equals(status.Leader, statuses[0].Leader,
            StringComparison.Ordinal))).IsTrue();
        var leader = new Uri(statuses[0].Leader ?? throw new InvalidOperationException("RF3 leader was absent."));
        return Enumerable.Range(0, Nodes.Length).Single(index => string.Equals(leader.Host, Nodes[index], StringComparison.Ordinal));
    }

    private static async Task VerifyClusterCatchupAsync(GraphSearchRf3Scenario scenario,
        GraphSearchRf3NodeClients clients, CommandRequest addition, CommitReceipt receipt, CancellationToken cancellationToken)
    {
        foreach (var node in Nodes)
        {
            await WaitReadyAsync(clients.Administrators[Array.IndexOf(Nodes, node)], cancellationToken);
        }
        for (var index = 0; index < Nodes.Length; index++)
        {
            await GraphSearchRf3DurableAddition.ReplayAsync(clients.Administrators[index], addition, receipt, cancellationToken);
            await GraphSearchRf3LeaderLossLiterals.GraphAsync(scenario, clients.Administrators[index], cancellationToken);
            var result = await McpCallerAssertions.SdkSuccessAsync(await clients.Readers[index].GraphSearchAsync(
                GraphSearchRf3Scenario.Request(scenario.Partition), cancellationToken));
            await GraphSearchRf3Assertions.AssertHitsAsync(result,
                (GraphSearchRf3Scenario.Alpha, 1d / 61), (GraphSearchRf3Scenario.Beta, 1d / 62),
                ("delta", 1d / 63), (GraphSearchRf3Scenario.Gamma, 1d / 64));
            await GraphSearchRf3LeaderLossLiterals.HitsAsync(scenario, result, added: true);
        }
    }

    private static async Task WaitReadyAsync(KeyLoadClient administrator, CancellationToken cancellationToken)
        => await ClusterReplicationTestSupport.EventuallyAsync(async () =>
        {
            var status = await administrator.StatusAsync(cancellationToken);
            return status.IsSuccess && status.Value!.RoutingReady && status.Value.Voters == Nodes.Length;
        }, cancellationToken);

    private async Task RestoreNodeAsync(string node, KeyLoadClient[] administrators, Exception? primary)
    {
        try
        {
            using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(45), TimeProvider.System);
            await fixture.RestartContainerAsync(node, recovery.Token);
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(node,
                WaitBehavior.WaitOnResourceUnavailable, recovery.Token);
            foreach (var admin in administrators)
            {
                await WaitReadyAsync(admin, recovery.Token);
            }
        }
        catch (Exception cleanup) when (ClusterReplicationTestSupport.IsNonFatalCleanupFailure(cleanup))
        {
            if (primary is null)
            {
                throw;
            }
            primary.Data[RestartFailureKey] = cleanup;
        }
    }

    private async Task CaptureDiagnosticsAsync(Exception failure)
    {
        try
        {
            await fixture.SaveFailureDiagnosticsAsync();
        }
        catch (Exception cleanup) when (ClusterReplicationTestSupport.IsNonFatalCleanupFailure(cleanup))
        {
            failure.Data[DiagnosticsFailureKey] = cleanup;
        }
    }
}
