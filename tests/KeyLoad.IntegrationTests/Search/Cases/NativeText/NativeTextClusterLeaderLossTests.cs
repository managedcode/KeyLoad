using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.Search;

/// <summary>AC-FTS-005: real CLUSTER search remains tied to canonical state after leader loss and restart.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class NativeTextClusterLeaderLossTests(ClusterFixture fixture)
{
    private const int ReplicaCount = 3;
    private const string MissingLeader = "The CLUSTER cluster did not advertise a leader.";
    private const string RestartFailureKey = "nativeTextRestartFailure";
    private const string DiagnosticsFailureKey = "nativeTextDiagnosticsFailure";

    [Test]
    public async Task AcFts005UpdatedTextAndNativeProjectionRecoverAcrossLeaderLossAndRestart()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await NativeTextClusterScenario.CreateAsync(fixture, deadline.Token);
        var identity = await scenario.CreateReaderAsync(fixture, true, true, false, false, deadline.Token);
        var nodes = new[] { McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3 };
        using var clients = new NativeTextClusterClients(fixture, identity.Secret, nodes);
        var update = await UpdateCorpusAsync(scenario, clients.Administrators[0], deadline.Token);
        await StopAndVerifyAsync(scenario, nodes, clients, update, deadline.Token);
    }

    private async Task StopAndVerifyAsync(NativeTextClusterScenario scenario, string[] nodes,
        NativeTextClusterClients clients, (CommandRequest Command, CommitReceipt Receipt) update,
        CancellationToken cancellationToken)
    {
        string? stoppedNode = null;
        var restarted = false;
        Exception? primaryFailure = null;
        try
        {
            var leaderIndex = await FindLeaderIndexAsync(nodes, clients.Administrators, cancellationToken);
            stoppedNode = nodes[leaderIndex];
            await fixture.KillContainerAsync(stoppedNode, NativeTextClusterScenario.FailureScenario, cancellationToken);
            var survivorIndex = Enumerable.Range(0, nodes.Length).First(index => index != leaderIndex);
            await WaitReadyAsync(clients.Administrators[survivorIndex], cancellationToken);
            await VerifyUpdatedSearchAsync(scenario, clients.Callers[survivorIndex], clients.Secret,
                nodes[survivorIndex], cancellationToken);
            await NativeTextClusterReplayAssertions.ReplayAsync(fixture, nodes[survivorIndex],
                clients.Administrators[survivorIndex], update.Command, update.Receipt, cancellationToken);
            await VerifyUpdatedSearchAsync(scenario, clients.Callers[survivorIndex], clients.Secret,
                nodes[survivorIndex], cancellationToken);
            await fixture.RestartContainerAsync(stoppedNode, cancellationToken);
            restarted = true;
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(stoppedNode,
                WaitBehavior.WaitOnResourceUnavailable, cancellationToken);
            await WaitForClusterAsync(nodes, clients.Administrators, cancellationToken);
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

    private async Task VerifyUpdatedSearchAsync(NativeTextClusterScenario scenario, KeyLoadClient sdk,
        string identitySecret, string node, CancellationToken cancellationToken)
    {
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, node, identitySecret, cancellationToken);
        await NativeTextClusterReplayAssertions.ResultsAsync(scenario, sdk, mcp, cancellationToken);
    }

    private async Task VerifyAllNodesAsync(NativeTextClusterScenario scenario, string[] nodes,
        NativeTextClusterClients clients, CancellationToken cancellationToken)
    {
        for (var index = 0; index < nodes.Length; index++)
        {
            await VerifyUpdatedSearchAsync(scenario, clients.Callers[index], clients.Secret, nodes[index], cancellationToken);
        }
    }

    private static async Task<(CommandRequest Command, CommitReceipt Receipt)> UpdateCorpusAsync(NativeTextClusterScenario scenario, KeyLoadClient administrator,
        CancellationToken cancellationToken)
    {
        var update = new CommandRequest(Guid.NewGuid(), scenario.Partition,
        [
            new PutDocument(NativeTextClusterScenario.Collection, NativeTextClusterScenario.FirstId,
                NativeTextClusterReplayAssertions.UpdatedJson,
                NativeTextClusterScenario.FirstRevision, new(NativeTextClusterScenario.OwnerA)),
            new PutVector(NativeTextClusterScenario.Collection, NativeTextClusterScenario.FirstId,
                NativeTextClusterScenario.VectorField, [1, 0], NativeTextClusterScenario.SpaceFor(),
                NativeTextClusterScenario.UpdatedRevision),
            new DeleteDocument(NativeTextClusterScenario.Collection, NativeTextClusterScenario.SecondId,
                NativeTextClusterScenario.FirstRevision)
        ]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(update, cancellationToken));
        await NativeTextClusterReplayAssertions.ReceiptAsync(update, receipt);
        return (update, receipt);
    }

    private static async Task<int> FindLeaderIndexAsync(string[] nodes, KeyLoadClient[] administrators,
        CancellationToken cancellationToken)
    {
        var statuses = (await Task.WhenAll(administrators.Select(client => client.StatusAsync(cancellationToken))))
            .Select(ClusterReplicationTestSupport.Success).ToArray();
        await Assert.That(statuses.All(status => status.Voters == ReplicaCount)).IsTrue();
        await Assert.That(statuses.All(status => string.Equals(status.Leader, statuses[0].Leader,
            StringComparison.Ordinal))).IsTrue();
        var leader = new Uri(statuses[0].Leader ?? throw new InvalidOperationException(MissingLeader));
        return Enumerable.Range(0, nodes.Length).Single(index => string.Equals(leader.Host,
            nodes[index], StringComparison.Ordinal));
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
            return status.IsSuccess && status.Value!.RoutingReady;
        }, cancellationToken);

    private async Task RestoreStoppedNodeAsync(string stoppedNode, string[] nodes,
        KeyLoadClient[] administrators, Exception? primaryFailure)
    {
        try
        {
            using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(45), TimeProvider.System);
            await fixture.RestartContainerAsync(stoppedNode, recovery.Token);
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

    private sealed class NativeTextClusterClients : IDisposable
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

        internal NativeTextClusterClients(ClusterFixture fixture, string secret, string[] nodes)
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
