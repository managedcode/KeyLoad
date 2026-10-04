using System.Collections.Immutable;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleRetentionRf3RestartWorkflow
{
    private const string FailureScenario = "series-retention-follower-restart";
    private const string FollowerRestoreFailureKey = "KeyLoad.RetentionFollowerRestoreFailure";
    private const int NodeCount = TimeSeriesRf3Scenario.NodeCount;
    private const int FirstNode = TimeSeriesRf3Scenario.FirstNode;
    private const int McpNodeIndex = 1;

    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken callerToken)
    {
        using var deadline = McpCallerDeadline.Create();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, callerToken);
        var cancellationToken = linked.Token;
        var clients = CreateClients(fixture);
        var scenario = await PrepareAsync(fixture, cancellationToken);
        var leader = await SelectLeaderIndexAsync(clients, cancellationToken);
        var follower = await SelectRestartableFollowerAsync(clients, leader, cancellationToken);
        var node = ClusterReplicationTestSupport.NodeName(follower + FirstNode);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture,
            ClusterReplicationTestSupport.NodeName(McpNodeIndex + FirstNode), fixture.AdminKey, cancellationToken);
        await ExecuteWithRestartCleanupAsync(fixture,
            new(clients, scenario, leader, follower, node, mcp), cancellationToken);
    }

    private static async Task ExecuteWithRestartCleanupAsync(ClusterFixture fixture, RestartContext context,
        CancellationToken cancellationToken)
    {
        string? stoppedNode = null;
        var restarted = false;
        Exception? activeFailure = null;
        try
        {
            await ApplyFirstPageAsync(fixture, context, cancellationToken);
            stoppedNode = context.FollowerNode;
            await fixture.KillContainerAsync(stoppedNode, FailureScenario, cancellationToken);
            await ApplyQuorumPageAsync(context, cancellationToken);
            await fixture.RestartContainerAsync(stoppedNode, cancellationToken);
            restarted = true;
            await WaitForNodeAsync(fixture, context, cancellationToken);
            await SampleRetentionRf3RecoveryAssertions.VerifyAllNodesAsync(context.Clients,
                context.Mcp, context.Scenario, new(context.Cutoff, 3, false),
                ["retention-3", "at-retention-floor"], cancellationToken);
        }
        catch (Exception failure) when (ClusterReplicationTestSupport.IsNonFatalCleanupFailure(failure))
        {
            activeFailure = failure;
            await SaveDiagnosticsAsync(fixture, failure);
            throw;
        }
        finally
        {
            if (!restarted && stoppedNode is not null)
            {
                await RestoreFollowerAsync(fixture, context, stoppedNode, activeFailure);
            }
        }
    }

    private static async Task ApplyFirstPageAsync(ClusterFixture fixture, RestartContext context,
        CancellationToken cancellationToken)
    {
        var command = ExpireCommand(context, 1);
        var first = ClusterReplicationTestSupport.Success(await context.Clients[context.Leader]
            .CommitAsync(command, cancellationToken));
        var replay = ClusterReplicationTestSupport.Success(await context.Clients[(context.Leader + 1) % NodeCount]
            .CommitAsync(command, cancellationToken));
        await Assert.That(replay.Token).IsEqualTo(first.Token);
        await VerifyAllNodesAsync(context, new(context.Cutoff, 1, true), cancellationToken);
        await SampleRetentionRf3MutationAssertions.VerifyPersistedGrantsAsync(fixture, context.Mcp,
            context.Scenario, context.Cutoff, cancellationToken);
        await SampleRetentionRf3MutationAssertions.VerifyLateAppendBehaviorAsync(fixture, context.Mcp,
            context.Scenario, context.Cutoff, cancellationToken);
    }

    private static async Task ApplyQuorumPageAsync(RestartContext context, CancellationToken cancellationToken)
    {
        var command = ExpireCommand(context, 2);
        var result = ClusterReplicationTestSupport.Success(await ClusterReplicationTestSupport.RetryDuringElectionAsync(
            () => context.Clients[context.Leader].CommitAsync(command, cancellationToken), cancellationToken));
        await Assert.That(result.Mutations).HasSingleItem();
        var replay = ClusterReplicationTestSupport.Success(await ClusterReplicationTestSupport.RetryDuringElectionAsync(
            () => context.Clients[context.Leader].CommitAsync(command, cancellationToken), cancellationToken));
        await Assert.That(replay.Token).IsEqualTo(result.Token);
        await SampleRetentionRf3RecoveryAssertions.VerifyActiveNodesAsync(context.Clients,
            context.Follower, context.Mcp, context.Scenario, new(context.Cutoff, 3, false),
            ["retention-3", "at-retention-floor"], cancellationToken);
    }

    private static async Task VerifyAllNodesAsync(RestartContext context, SampleRetentionStatus status,
        CancellationToken cancellationToken)
        => await SampleRetentionRf3RecoveryAssertions.VerifyAllNodesAsync(context.Clients,
            context.Mcp, context.Scenario, status, ["retention-3"], cancellationToken);

    private static async Task<TimeSeriesRf3Scenario> PrepareAsync(ClusterFixture fixture,
        CancellationToken cancellationToken)
    {
        var scenario = await TimeSeriesRf3Scenario.CreateAsync(fixture, cancellationToken);
        var samples = Enumerable.Range(0, 4).Select(index => TimeSeriesRf3Scenario.Data(
            "retention-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            TimeSeriesRf3Scenario.Start.AddMinutes(index), Value(index))).ToImmutableArray();
        await scenario.AppendAsync(fixture, TimeSeriesRf3Scenario.Series, samples,
            TimeSeriesRf3Scenario.PublicTags, cancellationToken);
        return scenario;
    }

    private static KeyLoadClient[] CreateClients(ClusterFixture fixture)
        => Enumerable.Range(FirstNode, NodeCount)
            .Select(number => fixture.Client(ClusterReplicationTestSupport.NodeName(number))).ToArray();

    private static async Task<int> SelectLeaderIndexAsync(KeyLoadClient[] clients,
        CancellationToken cancellationToken)
    {
        var statuses = (await Task.WhenAll(clients.Select(client => client.StatusAsync(cancellationToken))))
            .Select(ClusterReplicationTestSupport.Success).ToArray();
        await Assert.That(statuses.All(status => status.Voters == NodeCount)).IsTrue();
        await Assert.That(statuses.All(status => string.Equals(status.Leader, statuses[0].Leader,
            StringComparison.Ordinal))).IsTrue();
        var leader = new Uri(statuses[0].Leader ?? throw new InvalidOperationException(
            "The initialized RF3 status omitted its leader URI."));
        return Enumerable.Range(0, NodeCount).Single(index => string.Equals(leader.Host,
            ClusterReplicationTestSupport.NodeName(index + FirstNode), StringComparison.Ordinal));
    }

    private static async Task<int> SelectRestartableFollowerAsync(KeyLoadClient[] clients, int leader,
        CancellationToken cancellationToken)
    {
        var follower = Enumerable.Range(0, NodeCount)
            .First(index => index != leader && index != McpNodeIndex);
        await Assert.That((await clients[follower].StatusAsync(cancellationToken)).IsSuccess).IsTrue();
        return follower;
    }

    private static async Task WaitForNodeAsync(ClusterFixture fixture, RestartContext context,
        CancellationToken cancellationToken)
    {
        await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(context.FollowerNode,
            WaitBehavior.WaitOnResourceUnavailable, cancellationToken);
        await ClusterReplicationTestSupport.EventuallyAsync(async () =>
        {
            var status = await context.Clients[context.Follower].StatusAsync(cancellationToken);
            return status.IsSuccess && status.Value!.RoutingReady && status.Value.Voters == NodeCount;
        }, cancellationToken);
    }

    private static async Task RestoreFollowerAsync(ClusterFixture fixture, RestartContext context,
        string node, Exception? activeFailure)
    {
        using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            await fixture.RestartContainerAsync(node, recovery.Token);
            await WaitForNodeAsync(fixture, context, recovery.Token);
        }
        catch (Exception cleanupFailure) when (ClusterReplicationTestSupport.IsNonFatalCleanupFailure(cleanupFailure))
        {
            if (activeFailure is not null)
            {
                activeFailure.Data[FollowerRestoreFailureKey] = cleanupFailure;
                return;
            }
            throw;
        }
    }

    private static async Task SaveDiagnosticsAsync(ClusterFixture fixture, Exception failure)
    {
        try
        {
            await fixture.SaveFailureDiagnosticsAsync();
        }
        catch (Exception diagnosticFailure) when (ClusterReplicationTestSupport.IsNonFatalCleanupFailure(diagnosticFailure))
        {
            failure.Data[ClusterReplicationTestSupport.Rf3DiagnosticsFailureKey] = diagnosticFailure;
        }
    }

    private static CommandRequest ExpireCommand(RestartContext context, int deletes)
        => new(Guid.NewGuid(), context.Scenario.Partition,
            [new ExpireSamples(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
                context.Cutoff, deletes)]);

    private static double Value(int index) => index switch
    {
        0 => 1,
        1 => 2,
        2 => 4,
        3 => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(index), index, "No retained sample is defined for this index.")
    };

    private sealed record RestartContext(KeyLoadClient[] Clients, TimeSeriesRf3Scenario Scenario,
        int Leader, int Follower, string FollowerNode, McpOfficialClient Mcp)
    {
        internal DateTimeOffset Cutoff { get; } = TimeSeriesRf3Scenario.Start.AddMinutes(3);
    }
}
