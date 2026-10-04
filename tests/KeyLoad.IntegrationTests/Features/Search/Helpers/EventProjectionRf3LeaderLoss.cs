using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed class EventProjectionRf3LeaderLoss(ClusterFixture fixture)
{
    private const int RestartDeadlineSeconds = 45;

    internal async Task RunAsync(EventProjectionRf3Scenario scenario, EventProjectionRf3Clients clients,
        CommitReceipt original, string[] nodes, CancellationToken cancellationToken)
    {
        string? stoppedNode = null;
        var restarted = false;
        Exception? primaryFailure = null;
        try
        {
            var leaderIndex = await FindLeaderIndexAsync(nodes, clients.Administrators, cancellationToken);
            stoppedNode = nodes[leaderIndex];
            await fixture.KillContainerAsync(stoppedNode, EventProjectionRf3Scenario.FailureScenario,
                cancellationToken);
            var survivor = Enumerable.Range(0, nodes.Length).First(index => index != leaderIndex);
            await WaitReadyAsync(clients.Administrators[survivor], cancellationToken);
            await ReplayAndCheckSurvivorAsync(scenario, clients, original, survivor, nodes[survivor],
                cancellationToken);
            await RestartAndCheckClusterAsync(stoppedNode, nodes, clients.Administrators, cancellationToken);
            restarted = true;
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
            { await RestoreStoppedNodeAsync(stoppedNode, nodes, clients.Administrators, primaryFailure); }
        }
    }

    private async Task ReplayAndCheckSurvivorAsync(EventProjectionRf3Scenario scenario,
        EventProjectionRf3Clients clients, CommitReceipt original, int survivor, string node,
        CancellationToken cancellationToken)
    {
        var retry = await ClusterReplicationTestSupport.RetryDuringElectionAsync(
            () => clients.Callers[survivor].CommitAsync(scenario.ApplyCommand(), cancellationToken), cancellationToken);
        var replay = ClusterReplicationTestSupport.Success(retry);
        await Assert.That(JsonDefaults.Serialize(replay).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(original))).IsTrue();
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

    private async Task VerifyEveryNodeAsync(EventProjectionRf3Scenario scenario,
        EventProjectionRf3Clients clients, string[] nodes, CancellationToken cancellationToken)
    {
        foreach (var index in Enumerable.Range(0, nodes.Length))
        { await VerifySurvivorBothAsync(scenario, clients.Readers[index], nodes[index], cancellationToken); }
    }

    private async Task VerifySurvivorBothAsync(EventProjectionRf3Scenario scenario, KeyLoadClient reader,
        string node, CancellationToken cancellationToken)
    {
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, node,
            scenario.ReaderSecret, cancellationToken);
        await EventProjectionRf3Assertions.VerifyVectorBothAsync(reader, mcp, scenario.VectorSearch(),
            scenario.ReaderSecret,
            [(EventProjectionRf3Scenario.TargetId, 1d / 61d),
                (EventProjectionRf3Scenario.BaselineId, 1d / 62d)], cancellationToken);
    }

    private static async Task<int> FindLeaderIndexAsync(string[] nodes, KeyLoadClient[] administrators,
        CancellationToken cancellationToken)
    {
        var statuses = (await Task.WhenAll(administrators.Select(client => client.StatusAsync(cancellationToken))))
            .Select(ClusterReplicationTestSupport.Success).ToArray();
        await Assert.That(statuses.All(status => status.Voters == EventProjectionRf3Scenario.NodeCount)).IsTrue();
        await Assert.That(statuses.All(status => string.Equals(status.Leader, statuses[0].Leader,
            StringComparison.Ordinal))).IsTrue();
        var leader = new Uri(statuses[0].Leader ?? throw new InvalidOperationException(
            EventProjectionRf3Scenario.MissingLeader));
        return Enumerable.Range(0, nodes.Length).Single(index => string.Equals(leader.Host,
            nodes[index], StringComparison.Ordinal));
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
            using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(RestartDeadlineSeconds));
            await fixture.RestartContainerAsync(stoppedNode, recovery.Token);
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(stoppedNode,
                WaitBehavior.WaitOnResourceUnavailable, recovery.Token);
            await WaitForClusterAsync(nodes, administrators, recovery.Token);
        }
        catch (Exception failure) when (ClusterReplicationTestSupport.IsNonFatalCleanupFailure(failure))
        {
            if (primaryFailure is null)
            { throw; }
            primaryFailure.Data[EventProjectionRf3Scenario.RestartFailureKey] = failure;
        }
    }

    private async Task CaptureDiagnosticsAsync(Exception failure)
    {
        try
        { await fixture.SaveFailureDiagnosticsAsync(); }
        catch (Exception diagnostic) when (ClusterReplicationTestSupport.IsNonFatalCleanupFailure(diagnostic))
        { failure.Data[EventProjectionRf3Scenario.DiagnosticsFailureKey] = diagnostic; }
    }
}
