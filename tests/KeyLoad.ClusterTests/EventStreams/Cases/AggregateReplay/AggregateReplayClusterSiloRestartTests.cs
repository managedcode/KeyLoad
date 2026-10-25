using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.TestInfrastructure;
using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.StorageReplication;

namespace KeyLoad.ClusterTests.EventStreams;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class AggregateReplayClusterSiloRestartTests(ClusterFixture fixture)
{
    [Test]
    public async Task AcEvent008SnapshotTailCommandRecoversAcrossSiloLossAndProcessRestart()
    {
        await ExecuteAsync(interruptAcceptedRestart: false);
    }

    [Test]
    public async Task AcEvent008AcceptedRestartCancellationResumesSameReplacementAndPreservesReplay()
        => await ExecuteAsync(interruptAcceptedRestart: true);

    private async Task ExecuteAsync(bool interruptAcceptedRestart)
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await AggregateReplayClusterScenario.CreateAsync(fixture, deadline.Token);
        var nodes = new[] { McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3 };
        using var clients = new AggregateReplayClusterWorkerClients(fixture, scenario.WorkerSecret, nodes);
        var append = await scenario.AppendInitialAsync(clients.Items[0], Guid.NewGuid(), deadline.Token);
        await Assert.That(append.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(append.PersistedCopies).IsEqualTo(3);
        var snapshot = await McpCallerAssertions.SdkSuccessAsync(await clients.Items[0].CommitAsync(
            scenario.InitialSnapshotCommand(Guid.NewGuid()), deadline.Token));
        await Assert.That(snapshot.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(snapshot.PersistedCopies).IsEqualTo(3);
        await RunSiloRestartAsync(scenario, nodes, clients.Items, clients.AdminItems, interruptAcceptedRestart, deadline.Token);
    }

    private async Task RunSiloRestartAsync(AggregateReplayClusterScenario scenario, string[] nodes,
        KeyLoadClient[] clients, KeyLoadClient[] administrators, bool interruptAcceptedRestart, CancellationToken cancellationToken)
    {
        string? stoppedNode = null;
        var restarted = false;
        Exception? originalFailure = null;
        try
        {
            var selectedIndex = await SelectSiloIndexAsync(nodes, administrators, cancellationToken);
            stoppedNode = nodes[selectedIndex];
            await fixture.KillServerAsync(stoppedNode, AggregateReplayClusterTokens.FailureScenario, cancellationToken);
            var survivorIndex = Enumerable.Range(0, nodes.Length).First(index => index != selectedIndex);
            await WaitUntilReadyAsync(administrators[survivorIndex], cancellationToken);
            var frozen = scenario.FailoverCommand(Guid.NewGuid());
            await ClusterReplicationTestSupport.MissingCopiesAsync(await clients[survivorIndex].CommitAsync(frozen, cancellationToken));

            ProcessFaultCommandProtocol.LaunchIdentity? accepted = null;
            if (interruptAcceptedRestart)
            {
                accepted = await fixture.BeginProcessRestartAsync(stoppedNode, cancellationToken);
                await AcceptedRestartCancellationAssertions.InterruptAsync(fixture, stoppedNode, cancellationToken);
            }
            await fixture.RestartServerAsync(stoppedNode, cancellationToken);
            restarted = true;
            if (accepted is not null)
            {
                await AcceptedRestartCancellationAssertions.SameReplacementAsync(fixture, accepted, cancellationToken);
            }
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(stoppedNode,
                WaitBehavior.WaitOnResourceUnavailable, cancellationToken);
            await WaitForClusterReadyAsync(nodes, administrators, cancellationToken);
            var receipt = ClusterReplicationTestSupport.Success(await ClusterReplicationTestSupport.RetryDuringSiloRecoveryAsync(
                () => clients[survivorIndex].CommitAsync(frozen, cancellationToken), cancellationToken));
            await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
            await Assert.That(receipt.PersistedCopies).IsEqualTo(nodes.Length);
            await Assert.That(receipt.Mutations.Length).IsEqualTo(2);
            await SubmissionColdAssertions.ReceiptAsync(ClusterReplicationTestSupport.Success(
                await clients[survivorIndex].CommitAsync(frozen, cancellationToken)), receipt);
            await Assert.That(Directory.Exists(Path.Combine(fixture.Root, stoppedNode))).IsTrue();
            await VerifyAllNodeParityAsync(scenario, nodes, clients, cancellationToken);
            if (interruptAcceptedRestart)
            {
                await RestartReadinessEvidenceAssertions.HealthyAsync(fixture, cancellationToken);
            }
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
                using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(45), TimeProvider.System);
                await RestoreSiloAsync(stoppedNode, nodes, administrators, originalFailure, recovery.Token);
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
            failure.Data[AggregateReplayClusterTokens.DiagnosticsFailureKey] = diagnosticFailure;
        }
    }

    private static async Task<int> SelectSiloIndexAsync(string[] nodes, KeyLoadClient[] clients,
        CancellationToken cancellationToken)
    {
        var statuses = (await Task.WhenAll(clients.Select(client => client.StatusAsync(cancellationToken))))
            .Select(ClusterReplicationTestSupport.Success).ToArray();
        await Assert.That(statuses.All(status => status.RoutingReady && status.SiloCount == nodes.Length
            && status.ReplicaCount == nodes.Length)).IsTrue();
        await Assert.That(statuses.Select(status => status.NodeId).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(nodes.Length);
        return nodes.Length - 1;
    }

    private async Task VerifyAllNodeParityAsync(AggregateReplayClusterScenario scenario, string[] nodes,
        KeyLoadClient[] clients, CancellationToken cancellationToken)
    {
        foreach (var index in Enumerable.Range(0, nodes.Length))
        {
            var request = scenario.ReadRequest(maximumEvents: AggregateReplayClusterTokens.TailCount);
            var sdkPage = ClusterReplicationTestSupport.Success(
                await clients[index].ReadAggregateReplayAsync(request, cancellationToken));
            await using var mcp = await McpOfficialClient.ConnectAsync(fixture, nodes[index],
                scenario.WorkerSecret, cancellationToken);
            var mcpPage = await McpCallerAssertions.SuccessAsync<AggregateReplayPage>(await mcp.CallAsync(
                McpCallerTools.StreamsReplay, request, cancellationToken));
            await AggregateReplayClusterAssertions.SamePageAsync(sdkPage, mcpPage.Value,
                expectedCount: AggregateReplayClusterTokens.FailoverReducedCount,
                expectedTailCount: AggregateReplayClusterTokens.FailoverTailCount,
                expectedEventIds: [AggregateReplayClusterTokens.DispatchedEventId,
                    AggregateReplayClusterTokens.DeliveredEventId],
                expectedSnapshotState: AggregateReplayClusterTokens.FailoverState,
                expectedSnapshotVersion: AggregateReplayClusterTokens.SecondSnapshotVersion);
            await Assert.That(sdkPage.Snapshot!.SnapshotVersion)
                .IsEqualTo(AggregateReplayClusterTokens.SecondSnapshotVersion);
        }
    }

    private async Task RestoreSiloAsync(string stoppedNode, string[] nodes, KeyLoadClient[] administrators,
        Exception? originalFailure, CancellationToken cancellationToken)
    {
        try
        {
            await fixture.RestartServerAsync(stoppedNode, cancellationToken);
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(stoppedNode,
                WaitBehavior.WaitOnResourceUnavailable, cancellationToken);
            await Assert.That(Directory.Exists(Path.Combine(fixture.Root, stoppedNode))).IsTrue();
            await WaitForClusterReadyAsync(nodes, administrators, cancellationToken);
        }
        catch (Exception recoveryFailure) when (ClusterReplicationTestSupport.IsNonFatalCleanupFailure(recoveryFailure))
        {
            if (originalFailure is not null)
            {
                originalFailure.Data[AggregateReplayClusterTokens.RestartFailureKey] = recoveryFailure;
                return;
            }
            throw;
        }
    }

    private static async Task WaitUntilReadyAsync(KeyLoadClient client, CancellationToken cancellationToken)
        => await ClusterReplicationTestSupport.EventuallyAsync(async () =>
        {
            var status = await client.StatusAsync(cancellationToken);
            return status.IsSuccess && status.Value.RoutingReady;
        }, cancellationToken);

    private static async Task WaitForClusterReadyAsync(string[] nodes, KeyLoadClient[] clients,
        CancellationToken cancellationToken)
    {
        foreach (var index in Enumerable.Range(0, nodes.Length))
        {
            await WaitUntilReadyAsync(clients[index], cancellationToken);
        }
    }

}
