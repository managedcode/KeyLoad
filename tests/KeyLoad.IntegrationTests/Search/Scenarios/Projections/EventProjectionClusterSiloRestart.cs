using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed class EventProjectionClusterSiloRestart(ClusterFixture fixture)
{
    private const int RestartDeadlineSeconds = 45;

    internal async Task RunAsync(EventProjectionClusterScenario scenario, EventProjectionClusterClients clients,
        CommitReceipt original, string[] nodes, CancellationToken cancellationToken)
    {
        string? stoppedNode = null;
        var restarted = false;
        Exception? primaryFailure = null;
        try
        {
            var selectedIndex = await SelectSiloIndexAsync(nodes, clients.Administrators, cancellationToken);
            stoppedNode = nodes[selectedIndex];
            await fixture.KillContainerAsync(stoppedNode, EventProjectionClusterScenario.FailureScenario,
                cancellationToken);
            var survivor = Enumerable.Range(0, nodes.Length).First(index => index != selectedIndex);
            await WaitReadyAsync(clients.Administrators[survivor], cancellationToken);
            await ReplayAndCheckSurvivorAsync(scenario, clients, original, survivor, nodes[survivor],
                cancellationToken);
            await RestartAndCheckClusterAsync(stoppedNode, nodes, clients.Administrators, cancellationToken);
            restarted = true;
            var replay = ClusterReplicationTestSupport.Success(await clients.Callers[survivor].CommitAsync(
                scenario.ApplyCommand(), cancellationToken));
            await NativeSubmissionColdAssertions.ReceiptAsync(replay, original);
            await VerifyEveryNodeAsync(scenario, clients, nodes, cancellationToken);
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

    private async Task ReplayAndCheckSurvivorAsync(EventProjectionClusterScenario scenario,
        EventProjectionClusterClients clients, CommitReceipt original, int survivor, string node,
        CancellationToken cancellationToken)
    {
        var replay = await clients.Callers[survivor].CommitAsync(scenario.ApplyCommand(), cancellationToken);
        await Assert.That(replay.IsFailed).IsTrue();
        await Assert.That(replay.Value).IsNull();
        await Assert.That(replay.Problem?.ErrorCode is nameof(ErrorCode.OwnershipLost)
            or nameof(ErrorCode.UnknownWriteOutcome)).IsTrue();
        await VerifySurvivorBothAsync(scenario, clients.Readers[survivor], node, cancellationToken);
    }

    private async Task RestartAndCheckClusterAsync(string stoppedNode, string[] nodes,
        KeyLoadClient[] administrators, CancellationToken cancellationToken)
    {
        await fixture.RestartContainerAsync(stoppedNode, cancellationToken);
        await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(stoppedNode,
            WaitBehavior.WaitOnResourceUnavailable, cancellationToken);
        await WaitForClusterAsync(nodes, administrators, cancellationToken);
    }

    private async Task VerifyEveryNodeAsync(EventProjectionClusterScenario scenario,
        EventProjectionClusterClients clients, string[] nodes, CancellationToken cancellationToken)
    {
        foreach (var index in Enumerable.Range(0, nodes.Length))
        {
            await VerifySurvivorBothAsync(scenario, clients.Readers[index], nodes[index], cancellationToken);
        }
    }

    private async Task VerifySurvivorBothAsync(EventProjectionClusterScenario scenario, KeyLoadClient reader,
        string node, CancellationToken cancellationToken)
    {
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, node,
            scenario.ReaderSecret, cancellationToken);
        await EventProjectionClusterAssertions.VerifyVectorBothAsync(reader, mcp, scenario.VectorSearch(),
            scenario.ReaderSecret,
            [(EventProjectionClusterScenario.TargetId, 1d / 61d),
                (EventProjectionClusterScenario.BaselineId, 1d / 62d)], cancellationToken);
    }

    private static async Task<int> SelectSiloIndexAsync(string[] nodes, KeyLoadClient[] administrators,
        CancellationToken cancellationToken)
    {
        var statuses = (await Task.WhenAll(administrators.Select(client => client.StatusAsync(cancellationToken))))
            .Select(ClusterReplicationTestSupport.Success).ToArray();
        await Assert.That(statuses.All(status => status.SiloCount == EventProjectionClusterScenario.NodeCount)).IsTrue();
        await Assert.That(statuses.All(status => status.RoutingReady && status.ReplicaCount == nodes.Length)).IsTrue();
        await Assert.That(statuses.Select(status => status.NodeId).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(nodes.Length);
        return nodes.Length - 1;
    }

    private async Task WaitForClusterAsync(string[] nodes, KeyLoadClient[] administrators,
        CancellationToken cancellationToken)
    {
        foreach (var index in Enumerable.Range(0, nodes.Length))
        {
            await WaitReadyAsync(administrators[index], cancellationToken);
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(nodes[index],
                WaitBehavior.WaitOnResourceUnavailable, cancellationToken);
        }
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
            using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(RestartDeadlineSeconds), TimeProvider.System);
            await fixture.RestartContainerAsync(stoppedNode, recovery.Token);
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(stoppedNode,
                WaitBehavior.WaitOnResourceUnavailable, recovery.Token);
            await WaitForClusterAsync(nodes, administrators, recovery.Token);
        }
        catch (Exception failure) when (ClusterReplicationTestSupport.IsNonFatalCleanupFailure(failure))
        {
            if (primaryFailure is null)
            {
                throw;
            }
            primaryFailure.Data[EventProjectionClusterScenario.RestartFailureKey] = failure;
        }
    }

    private async Task CaptureDiagnosticsAsync(Exception failure)
    {
        try
        {
            await fixture.SaveFailureDiagnosticsAsync();
        }
        catch (Exception diagnostic) when (ClusterReplicationTestSupport.IsNonFatalCleanupFailure(diagnostic))
        {
            failure.Data[EventProjectionClusterScenario.DiagnosticsFailureKey] = diagnostic;
        }
    }
}
