using System.Collections.Immutable;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.StorageReplication;

namespace KeyLoad.IntegrationTests.TimeSeries;

internal static class SampleRetentionClusterRestartWorkflow
{
    private const string FailureScenario = "series-retention-silo-restart";
    private const string SiloRestoreFailureKey = "KeyLoad.RetentionFollowerRestoreFailure";
    private const int NodeCount = TimeSeriesClusterScenario.NodeCount;
    private const int FirstNode = TimeSeriesClusterScenario.FirstNode;

    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken callerToken)
    {
        using var deadline = McpCallerDeadline.Create();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, callerToken);
        var cancellationToken = linked.Token;
        var clients = CreateClients(fixture);
        var scenario = await PrepareAsync(fixture, cancellationToken);
        var writer = await SelectWriterIndexAsync(clients, cancellationToken);
        var selectedSilo = await SelectRestartableSiloAsync(clients, writer, cancellationToken);
        var node = ClusterReplicationTestSupport.NodeName(selectedSilo + FirstNode);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture,
            ClusterReplicationTestSupport.NodeName(1 + FirstNode), fixture.AdminKey, cancellationToken);
        await ExecuteWithRestartCleanupAsync(fixture,
            new(clients, scenario, writer, selectedSilo, node, mcp), cancellationToken);
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
            stoppedNode = context.SelectedSiloNode;
            await fixture.KillServerAsync(stoppedNode, FailureScenario, cancellationToken);
            var command = ExpireCommand(context, 2);
            await ClusterReplicationTestSupport.MissingCopiesAsync(await context.Clients[context.Writer]
                .CommitAsync(command, cancellationToken));
            await fixture.RestartServerAsync(stoppedNode, cancellationToken);
            restarted = true;
            await WaitForNodeAsync(fixture, context, cancellationToken);
            await ApplyCopiedPageAsync(context, command, cancellationToken);
            await SampleRetentionClusterRecoveryAssertions.VerifyAllNodesAsync(context.Clients,
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
                await RestoreSiloAsync(fixture, context, stoppedNode, activeFailure);
            }
        }
    }

    private static async Task ApplyFirstPageAsync(ClusterFixture fixture, RestartContext context,
        CancellationToken cancellationToken)
    {
        var command = ExpireCommand(context, 1);
        var first = ClusterReplicationTestSupport.Success(await context.Clients[context.Writer]
            .CommitAsync(command, cancellationToken));
        var replay = ClusterReplicationTestSupport.Success(await context.Clients[(context.Writer + 1) % NodeCount]
            .CommitAsync(command, cancellationToken));
        await Assert.That(replay.Token).IsEqualTo(first.Token);
        await VerifyAllNodesAsync(context, new(context.Cutoff, 1, true), cancellationToken);
        await SampleRetentionClusterMutationAssertions.VerifyPersistedGrantsAsync(fixture, context.Mcp,
            context.Scenario, context.Cutoff, cancellationToken);
        await SampleRetentionClusterMutationAssertions.VerifyLateAppendBehaviorAsync(fixture, context.Mcp,
            context.Scenario, context.Cutoff, cancellationToken);
    }

    private static async Task ApplyCopiedPageAsync(RestartContext context, CommandRequest command,
        CancellationToken cancellationToken)
    {
        var result = ClusterReplicationTestSupport.Success(await ClusterReplicationTestSupport.RetryDuringSiloRecoveryAsync(
            () => context.Clients[context.Writer].CommitAsync(command, cancellationToken), cancellationToken));
        await Assert.That(result.Mutations).HasSingleItem();
        await Assert.That(result.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(result.PersistedCopies).IsEqualTo(NodeCount);
        var replay = ClusterReplicationTestSupport.Success(await ClusterReplicationTestSupport.RetryDuringSiloRecoveryAsync(
            () => context.Clients[context.Writer].CommitAsync(command, cancellationToken), cancellationToken));
        await Assert.That(replay.Token).IsEqualTo(result.Token);
        await SampleRetentionClusterRecoveryAssertions.VerifyActiveNodesAsync(context.Clients,
            context.SelectedSilo, context.Mcp, context.Scenario, new(context.Cutoff, 3, false),
            ["retention-3", "at-retention-floor"], cancellationToken);
    }

    private static async Task VerifyAllNodesAsync(RestartContext context, SampleRetentionStatus status,
        CancellationToken cancellationToken)
        => await SampleRetentionClusterRecoveryAssertions.VerifyAllNodesAsync(context.Clients,
            context.Mcp, context.Scenario, status, ["retention-3"], cancellationToken);

    private static async Task<TimeSeriesClusterScenario> PrepareAsync(ClusterFixture fixture,
        CancellationToken cancellationToken)
    {
        var scenario = await TimeSeriesClusterScenario.CreateAsync(fixture, cancellationToken);
        var samples = Enumerable.Range(0, 4).Select(index => TimeSeriesClusterScenario.Data(
            "retention-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            TimeSeriesClusterScenario.Start.AddMinutes(index), Value(index))).ToImmutableArray();
        await scenario.AppendAsync(fixture, TimeSeriesClusterScenario.Series, samples,
            TimeSeriesClusterScenario.PublicTags, cancellationToken);
        return scenario;
    }

    private static KeyLoadClient[] CreateClients(ClusterFixture fixture)
        => Enumerable.Range(FirstNode, NodeCount)
            .Select(number => fixture.Client(ClusterReplicationTestSupport.NodeName(number))).ToArray();

    private static async Task<int> SelectWriterIndexAsync(KeyLoadClient[] clients,
        CancellationToken cancellationToken)
    {
        var statuses = (await Task.WhenAll(clients.Select(client => client.StatusAsync(cancellationToken))))
            .Select(ClusterReplicationTestSupport.Success).ToArray();
        await Assert.That(statuses.All(status => status.RoutingReady && status.SiloCount == NodeCount
            && status.ReplicaCount == NodeCount)).IsTrue();
        await Assert.That(statuses.Select(status => status.NodeId).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(NodeCount);
        return 0;
    }

    private static async Task<int> SelectRestartableSiloAsync(KeyLoadClient[] clients, int writer,
        CancellationToken cancellationToken)
    {
        var selectedSilo = Enumerable.Range(0, NodeCount)
            .First(index => index != writer && index != 1);
        await Assert.That((await clients[selectedSilo].StatusAsync(cancellationToken)).IsSuccess).IsTrue();
        return selectedSilo;
    }

    private static async Task WaitForNodeAsync(ClusterFixture fixture, RestartContext context,
        CancellationToken cancellationToken)
    {
        await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(context.SelectedSiloNode,
            WaitBehavior.WaitOnResourceUnavailable, cancellationToken);
        await ClusterReplicationTestSupport.EventuallyAsync(async () =>
        {
            var status = await context.Clients[context.SelectedSilo].StatusAsync(cancellationToken);
            return status.IsSuccess && status.Value.RoutingReady && status.Value.SiloCount == NodeCount;
        }, cancellationToken);
    }

    private static async Task RestoreSiloAsync(ClusterFixture fixture, RestartContext context,
        string node, Exception? activeFailure)
    {
        using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(45), TimeProvider.System);
        try
        {
            await fixture.RestartServerAsync(node, recovery.Token);
            await WaitForNodeAsync(fixture, context, recovery.Token);
        }
        catch (Exception cleanupFailure) when (ClusterReplicationTestSupport.IsNonFatalCleanupFailure(cleanupFailure))
        {
            if (activeFailure is not null)
            {
                activeFailure.Data[SiloRestoreFailureKey] = cleanupFailure;
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
            failure.Data[ClusterReplicationTestSupport.ClusterDiagnosticsFailureKey] = diagnosticFailure;
        }
    }

    private static CommandRequest ExpireCommand(RestartContext context, int deletes)
        => new(Guid.NewGuid(), context.Scenario.Partition,
            [new ExpireSamples(TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series,
                context.Cutoff, deletes)]);

    private static double Value(int index) => index switch
    {
        0 => 1,
        1 => 2,
        2 => 4,
        3 => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(index), index, "No retained sample is defined for this index.")
    };

    private sealed record RestartContext(KeyLoadClient[] Clients, TimeSeriesClusterScenario Scenario,
        int Writer, int SelectedSilo, string SelectedSiloNode, McpOfficialClient Mcp)
    {
        internal DateTimeOffset Cutoff { get; } = TimeSeriesClusterScenario.Start.AddMinutes(3);
    }
}
