using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.StorageReplication;

namespace KeyLoad.IntegrationTests.Search;

/// <summary>AC-FTS-005: real Orleans search remains tied to canonical state after silo loss and restart.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class TextClusterSiloRestartTests(ClusterFixture fixture)
{
    private const string RestartFailureKey = "textRestartFailure";
    private const string DiagnosticsFailureKey = "textDiagnosticsFailure";

    [Test]
    public async Task AcFts005UpdatedTextAndProjectionRecoverAcrossSiloLossAndRejoin()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await TextClusterScenario.CreateAsync(fixture, deadline.Token);
        var identity = await scenario.CreateReaderAsync(fixture, true, true, false, false, deadline.Token);
        var nodes = new[] { McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3 };
        using var clients = new TextClusterClients(fixture, identity.Secret, nodes);
        var update = await UpdateCorpusAsync(scenario, clients.Administrators[0], deadline.Token);
        await StopAndVerifyAsync(scenario, nodes, clients, update, deadline.Token);
    }

    private async Task StopAndVerifyAsync(TextClusterScenario scenario, string[] nodes,
        TextClusterClients clients, (CommandRequest Command, CommitReceipt Receipt) update,
        CancellationToken cancellationToken)
    {
        string? stoppedNode = null;
        var restarted = false;
        Exception? primaryFailure = null;
        try
        {
            var selectedIndex = await SelectSiloIndexAsync(nodes, clients.Administrators, cancellationToken);
            stoppedNode = nodes[selectedIndex];
            await fixture.KillServerAsync(stoppedNode, TextClusterScenario.FailureScenario, cancellationToken);
            var survivorIndex = Enumerable.Range(0, nodes.Length).First(index => index != selectedIndex);
            await WaitReadyAsync(clients.Administrators[survivorIndex], cancellationToken);
            await VerifyUpdatedSearchAsync(scenario, clients.Callers[survivorIndex], clients.Secret,
                nodes[survivorIndex], cancellationToken);
            await ClusterReplicationTestSupport.MissingCopiesAsync(
                await clients.Administrators[survivorIndex].CommitAsync(update.Command, cancellationToken));
            await VerifyUpdatedSearchAsync(scenario, clients.Callers[survivorIndex], clients.Secret,
                nodes[survivorIndex], cancellationToken);
            await fixture.RestartServerAsync(stoppedNode, cancellationToken);
            restarted = true;
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(stoppedNode,
                WaitBehavior.WaitOnResourceUnavailable, cancellationToken);
            await WaitForClusterAsync(nodes, clients.Administrators, cancellationToken);
            await TextClusterReplayAssertions.ReplayAsync(fixture, nodes[survivorIndex],
                clients.Administrators[survivorIndex], update.Command, update.Receipt, scenario.Lifetime, cancellationToken);
            await VerifyAllNodesAsync(scenario, nodes, clients, cancellationToken);
        }
        catch (Exception failure) when (ClusterReplicationTestSupport.IsNonFatalCleanupFailure(failure))
        {
            primaryFailure = failure;
            await CaptureDiagnosticsAsync(failure);
            throw;
        }
        finally
        {
            if (stoppedNode is not null && !restarted)
            {
                await RestoreStoppedNodeAsync(stoppedNode, nodes, clients.Administrators, primaryFailure);
            }
        }
    }

    private async Task VerifyUpdatedSearchAsync(TextClusterScenario scenario, KeyLoadClient sdk,
        string identitySecret, string node, CancellationToken cancellationToken)
    {
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, node, identitySecret, cancellationToken);
        await TextClusterReplayAssertions.ResultsAsync(scenario, sdk, mcp, cancellationToken);
    }

    private async Task VerifyAllNodesAsync(TextClusterScenario scenario, string[] nodes,
        TextClusterClients clients, CancellationToken cancellationToken)
    {
        for (var index = 0; index < nodes.Length; index++)
        {
            await VerifyUpdatedSearchAsync(scenario, clients.Callers[index], clients.Secret, nodes[index], cancellationToken);
        }
    }

    private static async Task<(CommandRequest Command, CommitReceipt Receipt)> UpdateCorpusAsync(TextClusterScenario scenario, KeyLoadClient administrator,
        CancellationToken cancellationToken)
    {
        var update = new CommandRequest(Guid.NewGuid(), scenario.Partition,
        [
            new PutDocument(TextClusterScenario.Collection, TextClusterScenario.FirstId,
                TextClusterReplayAssertions.UpdatedJson,
                TextClusterScenario.FirstRevision, new(TextClusterScenario.OwnerA)),
            new PutVector(TextClusterScenario.Collection, TextClusterScenario.FirstId,
                TextClusterScenario.VectorField, [1, 0], TextClusterScenario.SpaceFor(),
                TextClusterScenario.UpdatedRevision),
            new DeleteDocument(TextClusterScenario.Collection, TextClusterScenario.SecondId,
                TextClusterScenario.FirstRevision)
        ]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(update, cancellationToken));
        await TextClusterReplayAssertions.ReceiptAsync(update, receipt);
        return (update, receipt);
    }

    private static async Task<int> SelectSiloIndexAsync(string[] nodes, KeyLoadClient[] administrators,
        CancellationToken cancellationToken)
    {
        var statuses = (await Task.WhenAll(administrators.Select(client => client.StatusAsync(cancellationToken))))
            .Select(ClusterReplicationTestSupport.Success).ToArray();
        await Assert.That(statuses.All(status => status.RoutingReady && status.SiloCount == nodes.Length
            && status.ReplicaCount == nodes.Length)).IsTrue();
        await Assert.That(statuses.Select(status => status.NodeId).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(nodes.Length);
        return nodes.Length - 1;
    }

    private async Task WaitForClusterAsync(string[] nodes, KeyLoadClient[] administrators,
        CancellationToken cancellationToken)
    {
        foreach (var administrator in administrators)
        {
            await WaitReadyAsync(administrator, cancellationToken);
        }
        await Assert.That(nodes.All(node => Directory.Exists(Path.Combine(fixture.Root, node)))).IsTrue();
    }

    private static async Task WaitReadyAsync(KeyLoadClient administrator, CancellationToken cancellationToken)
        => await ClusterReplicationTestSupport.EventuallyAsync(async () =>
        {
            var status = await administrator.StatusAsync(cancellationToken);
            return status.IsSuccess && status.Value.RoutingReady;
        }, cancellationToken);

    private async Task RestoreStoppedNodeAsync(string stoppedNode, string[] nodes,
        KeyLoadClient[] administrators, Exception? primaryFailure)
    {
        try
        {
            using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(45), TimeProvider.System);
            await fixture.RestartServerAsync(stoppedNode, recovery.Token);
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(stoppedNode,
                WaitBehavior.WaitOnResourceUnavailable, recovery.Token);
            await WaitForClusterAsync(nodes, administrators, recovery.Token);
        }
        catch (Exception recoveryFailure) when (ClusterReplicationTestSupport.IsNonFatalCleanupFailure(recoveryFailure))
        {
            if (primaryFailure is null)
            {
                throw;
            }
            primaryFailure.Data[RestartFailureKey] = recoveryFailure;
        }
    }

    private async Task CaptureDiagnosticsAsync(Exception failure)
    {
        try
        {
            await fixture.SaveFailureDiagnosticsAsync();
        }
        catch (Exception diagnosticFailure) when (ClusterReplicationTestSupport.IsNonFatalCleanupFailure(diagnosticFailure))
        {
            failure.Data[DiagnosticsFailureKey] = diagnosticFailure;
        }
    }

    private sealed class TextClusterClients : IDisposable
    {
        private readonly HttpClient[] callerHttp;
        private readonly HttpClient[] adminHttp;
        internal KeyLoadClient[] Callers
        {
            get;
        }
        internal KeyLoadClient[] Administrators
        {
            get;
        }
        internal string Secret
        {
            get;
        }

        internal TextClusterClients(ClusterFixture fixture, string secret, string[] nodes)
        {
            Secret = secret;
            callerHttp = nodes.Select(node => McpCallerHttp.Create(fixture, node)).ToArray();
            adminHttp = nodes.Select(node => McpCallerHttp.Create(fixture, node)).ToArray();
            Callers = callerHttp.Select(client => new KeyLoadClient(client, secret, IntegrationClientOptions.Execution())).ToArray();
            Administrators = adminHttp.Select(client => new KeyLoadClient(client, fixture.AdminKey, IntegrationClientOptions.Execution())).ToArray();
        }

        public void Dispose()
        {
            foreach (var client in callerHttp)
            {
                client.Dispose();
            }
            foreach (var client in adminHttp)
            {
                client.Dispose();
            }
        }
    }
}
