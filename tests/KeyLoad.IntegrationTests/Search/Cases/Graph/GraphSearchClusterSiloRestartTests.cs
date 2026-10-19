using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.StorageReplication;

namespace KeyLoad.IntegrationTests.Features.Search;

/// <summary>AC-GSEARCH-006: committed graph writes remain readable across a scoped Orleans silo loss.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class GraphSearchClusterSiloRestartTests(ClusterFixture fixture)
{
    private const string RestartFailureKey = "graphSearchRestartFailure";
    private const string DiagnosticsFailureKey = "graphSearchDiagnosticsFailure";
    private static readonly string[] Nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];

    [Test]
    public async Task AcGsearch006CommittedGraphMutationSurvivesSiloLossAndRejoin()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await GraphSearchClusterScenario.CreateAsync(fixture, deadline.Token);
        var identity = await scenario.CreateReaderAsync(fixture, deadline.Token);
        using var clients = new GraphSearchClusterNodeClients(fixture, identity.Secret, Nodes);
        await RunLossAndRecoveryAsync(scenario, clients, deadline.Token);
    }

    private async Task RunLossAndRecoveryAsync(GraphSearchClusterScenario scenario,
        GraphSearchClusterNodeClients clients, CancellationToken cancellationToken)
    {
        string? stoppedNode = null;
        var restarted = false;
        Exception? primary = null;
        var addition = GraphSearchClusterDurableAddition.Create(scenario);
        try
        {
            var selectedIndex = await SelectSiloIndexAsync(clients.Administrators, cancellationToken);
            stoppedNode = Nodes[selectedIndex];
            await fixture.KillServerAsync(stoppedNode, "graph-search-cluster-silo-restart", cancellationToken);
            var survivor = await GraphSearchClusterSurvivorReadiness.WaitAsync(scenario,
                clients.Administrators, Nodes, selectedIndex, cancellationToken);
            var original = await McpCallerAssertions.SdkSuccessAsync(await clients.Readers[survivor]
                .GraphSearchAsync(GraphSearchClusterScenario.Request(scenario.Partition), cancellationToken));
            await GraphSearchClusterSiloRestartLiterals.HitsAsync(scenario, original, added: false);
            await ClusterReplicationTestSupport.MissingCopiesAsync(
                await clients.Administrators[survivor].CommitAsync(addition, cancellationToken));
            await fixture.RestartServerAsync(stoppedNode, cancellationToken);
            restarted = true;
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(stoppedNode,
                WaitBehavior.WaitOnResourceUnavailable, cancellationToken);
            var receipt = await WriteAndReadOnSurvivorAsync(scenario, clients, survivor, addition, cancellationToken);
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

    private async Task<CommitReceipt> WriteAndReadOnSurvivorAsync(GraphSearchClusterScenario scenario,
        GraphSearchClusterNodeClients clients, int survivor, CommandRequest addition, CancellationToken cancellationToken)
    {
        var receipt = await GraphSearchClusterDurableAddition.CommitAsync(clients.Administrators[survivor], addition, cancellationToken);
        await GraphSearchClusterDurableAddition.ReplayAsync(clients.Administrators[survivor], addition, receipt, cancellationToken);
        await using (var adminMcp = await McpOfficialClient.ConnectAsync(fixture, Nodes[survivor], fixture.AdminKey, cancellationToken))
        {
            var replay = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await adminMcp.CallAsync(
                McpCallerTools.DocumentsCommit, addition, cancellationToken));
            await Assert.That(JsonDefaults.Serialize(replay.Value).AsSpan().SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
        }
        var request = GraphSearchClusterScenario.Request(scenario.Partition);
        var result = await McpCallerAssertions.SdkSuccessAsync(await clients.Readers[survivor]
            .GraphSearchAsync(request, cancellationToken));
        await GraphSearchClusterAssertions.AssertHitsAsync(result,
            (GraphSearchClusterScenario.Alpha, 1d / 61), (GraphSearchClusterScenario.Beta, 1d / 62),
            ("delta", 1d / 63), (GraphSearchClusterScenario.Gamma, 1d / 64));
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, Nodes[survivor],
            clients.ReaderSecret, cancellationToken);
        var official = await McpCallerAssertions.SuccessAsync<GraphSearchResult>(await mcp.CallAsync(
            GraphSearchClusterScenario.SearchGraphTool, request, cancellationToken));
        await GraphSearchClusterAssertions.AssertEquivalentAsync(result, official.Value);
        await GraphSearchClusterSiloRestartLiterals.HitsAsync(scenario, result, added: true);
        await GraphSearchClusterSiloRestartLiterals.HitsAsync(scenario, official.Value, added: true);
        await GraphSearchClusterSiloRestartLiterals.GraphAsync(scenario, clients.Administrators[survivor], cancellationToken);
        return receipt;
    }

    private static async Task<int> SelectSiloIndexAsync(KeyLoadClient[] administrators,
        CancellationToken cancellationToken)
    {
        var statuses = (await Task.WhenAll(administrators.Select(client => client.StatusAsync(cancellationToken))))
            .Select(ClusterReplicationTestSupport.Success).ToArray();
        await Assert.That(statuses.All(status => status.RoutingReady && status.SiloCount == Nodes.Length
            && status.ReplicaCount == Nodes.Length)).IsTrue();
        await Assert.That(statuses.Select(status => status.NodeId).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(Nodes.Length);
        return Nodes.Length - 1;
    }

    private static async Task VerifyClusterCatchupAsync(GraphSearchClusterScenario scenario,
        GraphSearchClusterNodeClients clients, CommandRequest addition, CommitReceipt receipt, CancellationToken cancellationToken)
    {
        foreach (var node in Nodes)
        {
            await WaitReadyAsync(clients.Administrators[Array.IndexOf(Nodes, node)], cancellationToken);
        }
        for (var index = 0; index < Nodes.Length; index++)
        {
            await GraphSearchClusterDurableAddition.ReplayAsync(clients.Administrators[index], addition, receipt, cancellationToken);
            await GraphSearchClusterSiloRestartLiterals.GraphAsync(scenario, clients.Administrators[index], cancellationToken);
            var result = await McpCallerAssertions.SdkSuccessAsync(await clients.Readers[index].GraphSearchAsync(
                GraphSearchClusterScenario.Request(scenario.Partition), cancellationToken));
            await GraphSearchClusterAssertions.AssertHitsAsync(result,
                (GraphSearchClusterScenario.Alpha, 1d / 61), (GraphSearchClusterScenario.Beta, 1d / 62),
                ("delta", 1d / 63), (GraphSearchClusterScenario.Gamma, 1d / 64));
            await GraphSearchClusterSiloRestartLiterals.HitsAsync(scenario, result, added: true);
        }
    }

    private static async Task WaitReadyAsync(KeyLoadClient administrator, CancellationToken cancellationToken)
        => await ClusterReplicationTestSupport.EventuallyAsync(async () =>
        {
            var status = await administrator.StatusAsync(cancellationToken);
            return status.IsSuccess && status.Value!.RoutingReady && status.Value.SiloCount == Nodes.Length;
        }, cancellationToken);

    private async Task RestoreNodeAsync(string node, KeyLoadClient[] administrators, Exception? primary)
    {
        try
        {
            using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(45), TimeProvider.System);
            await fixture.RestartServerAsync(node, recovery.Token);
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
