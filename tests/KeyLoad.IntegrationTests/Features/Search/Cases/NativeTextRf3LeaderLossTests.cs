using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.Search;

/// <summary>AC-FTS-005: real RF3 search remains tied to canonical state after leader loss and restart.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class NativeTextRf3LeaderLossTests(ClusterFixture fixture)
{
    private const int ReplicaCount = 3;
    private const string MissingLeader = "The RF3 cluster did not advertise a leader.";
    private const string RestartFailureKey = "nativeTextRestartFailure";
    private const string DiagnosticsFailureKey = "nativeTextDiagnosticsFailure";

    [Test]
    public async Task AcFts005UpdatedTextAndNativeProjectionRecoverAcrossLeaderLossAndRestart()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await NativeTextRf3Scenario.CreateAsync(fixture, deadline.Token);
        var identity = await scenario.CreateReaderAsync(fixture, true, true, false, false, deadline.Token);
        var nodes = new[] { McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3 };
        using var clients = new NativeTextRf3Clients(fixture, identity.Secret, nodes);
        await UpdateCorpusAsync(scenario, clients.Administrators[0], deadline.Token);
        await StopAndVerifyAsync(scenario, nodes, clients, deadline.Token);
    }

    private async Task StopAndVerifyAsync(NativeTextRf3Scenario scenario, string[] nodes,
        NativeTextRf3Clients clients, CancellationToken cancellationToken)
    {
        string? stoppedNode = null;
        var restarted = false;
        Exception? primaryFailure = null;
        try
        {
            var leaderIndex = await FindLeaderIndexAsync(nodes, clients.Administrators, cancellationToken);
            stoppedNode = nodes[leaderIndex];
            await fixture.KillContainerAsync(stoppedNode, NativeTextRf3Scenario.FailureScenario, cancellationToken);
            var survivorIndex = Enumerable.Range(0, nodes.Length).First(index => index != leaderIndex);
            await WaitReadyAsync(clients.Administrators[survivorIndex], cancellationToken);
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

    private async Task VerifyUpdatedSearchAsync(NativeTextRf3Scenario scenario, KeyLoadClient sdk,
        string identitySecret, string node, CancellationToken cancellationToken)
    {
        var empty = await McpCallerAssertions.SdkSuccessAsync(await sdk.SearchAsync(scenario.Text("needle"),
            cancellationToken));
        await Assert.That(empty).IsEmpty();
        var healthy = await McpCallerAssertions.SdkSuccessAsync(await sdk.SearchAsync(scenario.Text("fresh"),
            cancellationToken));
        await Assert.That(healthy).HasSingleItem();
        await Assert.That(healthy[0].Document.Reference.Id).IsEqualTo(NativeTextRf3Scenario.FirstId);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, node, identitySecret, cancellationToken);
        var official = await McpCallerAssertions.SuccessAsync<RankedDocument[]>(await mcp.CallAsync(
            McpCallerTools.SearchExecute, scenario.Text("fresh"), cancellationToken));
        await Assert.That(JsonDefaults.Serialize(healthy).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(official.Value))).IsTrue();
        await AssertProjectedAsync(healthy[0].Document);
    }

    private async Task VerifyAllNodesAsync(NativeTextRf3Scenario scenario, string[] nodes,
        NativeTextRf3Clients clients, CancellationToken cancellationToken)
    {
        for (var index = 0; index < nodes.Length; index++)
        {
            await VerifyUpdatedSearchAsync(scenario, clients.Callers[index], clients.Secret, nodes[index], cancellationToken);
        }
    }

    private static async Task UpdateCorpusAsync(NativeTextRf3Scenario scenario, KeyLoadClient administrator,
        CancellationToken cancellationToken)
    {
        var update = new CommandRequest(Guid.NewGuid(), scenario.Partition,
        [
            new PutDocument(NativeTextRf3Scenario.Collection, NativeTextRf3Scenario.FirstId,
                NativeTextRf3Scenario.Document("fresh wording", NativeTextRf3Scenario.OwnerA),
                NativeTextRf3Scenario.FirstRevision, new(NativeTextRf3Scenario.OwnerA)),
            new PutVector(NativeTextRf3Scenario.Collection, NativeTextRf3Scenario.FirstId,
                NativeTextRf3Scenario.VectorField, [1, 0], NativeTextRf3Scenario.SpaceFor(),
                NativeTextRf3Scenario.UpdatedRevision),
            new DeleteDocument(NativeTextRf3Scenario.Collection, NativeTextRf3Scenario.SecondId,
                NativeTextRf3Scenario.FirstRevision)
        ]);
        await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(update, cancellationToken));
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
            using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(45));
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

    private static async Task AssertProjectedAsync(DocumentResult document)
    {
        await Assert.That(document.Redacted).IsTrue();
        await Assert.That(document.RedactedFields).Contains(NativeTextRf3Scenario.SecretField);
        await Assert.That(document.Json.Contains(NativeTextRf3Scenario.Secret, StringComparison.Ordinal)).IsFalse();
    }

    private sealed class NativeTextRf3Clients : IDisposable
    {
        private readonly HttpClient[] callerHttp;
        private readonly HttpClient[] adminHttp;
        internal KeyLoadClient[] Callers { get; }
        internal KeyLoadClient[] Administrators { get; }
        internal string Secret { get; }

        internal NativeTextRf3Clients(ClusterFixture fixture, string secret, string[] nodes)
        {
            Secret = secret;
            callerHttp = nodes.Select(node => McpCallerHttp.Create(fixture, node)).ToArray();
            adminHttp = nodes.Select(node => McpCallerHttp.Create(fixture, node)).ToArray();
            Callers = callerHttp.Select(client => new KeyLoadClient(client, secret)).ToArray();
            Administrators = adminHttp.Select(client => new KeyLoadClient(client, fixture.AdminKey)).ToArray();
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
