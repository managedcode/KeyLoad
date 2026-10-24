using System.Globalization;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.StorageReplication;

namespace KeyLoad.IntegrationTests.TimeSeries;

/// <summary>AC-SERIES-012: a real silo restart retains persisted series data on Orleans.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class TimeSeriesClusterReplicaRestartTests(ClusterFixture fixture)
{
    private const string FailureScenario = "series-silo-restart";
    private const string RestartFailureKey = "KeyLoad.TimeSeriesSiloRestartFailure";
    private const string InitialSampleId = "initial";
    private const string LateSampleId = "late";
    private const string LatestFailureMessage = "Latest-sample read failed: {0}; {1}.";
    private const string WindowsFailureMessage = "Window aggregate read failed: {0}; {1}.";
    private const string MissingProblemCode = "MissingProblem";
    private const string MissingSafeDetail = "No safe detail returned.";
    private const int NodeCount = TimeSeriesClusterScenario.NodeCount;
    private const int FirstNode = TimeSeriesClusterScenario.FirstNode;
    private const int ReadinessSeconds = 45;
    private static readonly CompositeFormat LatestFailureFormat = CompositeFormat.Parse(LatestFailureMessage);
    private static readonly CompositeFormat WindowsFailureFormat = CompositeFormat.Parse(WindowsFailureMessage);
    private static readonly TimeSpan OriginalTestLifetime = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ReadinessLifetime = TimeSpan.FromSeconds(ReadinessSeconds);

    [Test]
    public async Task AcSeries012SiloRestartRetainsLatestAndWindowValuesOnEveryReplica()
    {
        using var deadline = new CancellationTokenSource(OriginalTestLifetime, TimeProvider.System);
        using var firstHttp = McpCallerHttp.Create(fixture, TimeSeriesClusterScenario.Node1);
        using var secondHttp = McpCallerHttp.Create(fixture, TimeSeriesClusterScenario.Node2);
        using var thirdHttp = McpCallerHttp.Create(fixture, TimeSeriesClusterScenario.Node3);
        var clients = new[] { firstHttp, secondHttp, thirdHttp }
            .Select(http => new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution())).ToArray();
        string? stoppedNode = null;
        var restarted = false;
        try
        {
            var scenario = await PrepareSeriesAsync(fixture, deadline.Token);
            var selectedIndex = await SelectSiloIndexAsync(clients, deadline.Token);
            var selectedNode = NodeName(selectedIndex + FirstNode);
            await fixture.KillServerAsync(selectedNode, FailureScenario, deadline.Token);
            stoppedNode = selectedNode;

            var survivorIndex = Enumerable.Range(0, NodeCount).First(index => index != selectedIndex);
            var late = TimeSeriesClusterScenario.Data(LateSampleId, TimeSeriesClusterScenario.Start.AddMinutes(1), -7);
            var command = new CommandRequest(Guid.NewGuid(), scenario.Partition,
                [new AppendSamples(TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series, [late],
                    TimeSeriesClusterScenario.PublicTags)]);
            var unavailable = await clients[survivorIndex].CommitAsync(command, deadline.Token);
            await Assert.That(unavailable.IsFailed).IsTrue();
            await Assert.That(unavailable.Value).IsNull();
            await Assert.That(unavailable.Problem?.ErrorCode is nameof(ErrorCode.OwnershipLost)
                or nameof(ErrorCode.UnknownWriteOutcome)).IsTrue();

            var initial = ClusterReplicationTestSupport.Success(await clients[survivorIndex].ReadLatestSampleAsync(
                scenario.Latest(), deadline.Token));
            await Assert.That(initial.Sample?.Sample.EventId).IsEqualTo(InitialSampleId);
            await Assert.That(initial.Sample?.Sample.Value).IsEqualTo(3d);

            await fixture.RestartServerAsync(stoppedNode, deadline.Token);
            restarted = true;
            await ClusterReplicationTestSupport.EventuallyAsync(async () =>
            {
                var status = await clients[selectedIndex].StatusAsync(deadline.Token);
                return status.IsSuccess && status.Value!.RoutingReady;
            }, deadline.Token);
            await Assert.That(Directory.Exists(Path.Combine(fixture.Root, stoppedNode))).IsTrue();
            var committed = ClusterReplicationTestSupport.Success(await clients[survivorIndex].CommitAsync(command, deadline.Token));
            await Assert.That(committed.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
            await Assert.That(committed.PersistedCopies).IsEqualTo(NodeCount);
            await SubmissionColdAssertions.ReceiptAsync(ClusterReplicationTestSupport.Success(
                await clients[survivorIndex].CommitAsync(command, deadline.Token)), committed);
            foreach (var client in clients)
            {
                await VerifySeriesAsync(client, scenario, deadline.Token);
            }
        }
        catch (Exception failure) when (ClusterReplicationTestSupport.IsNonFatalCleanupFailure(failure))
        {
            await CaptureDiagnosticsAsync(failure);
            if (stoppedNode is not null && !restarted)
            {
                await RestoreSiloAsync(stoppedNode, clients, failure);
            }

            throw;
        }
    }

    private static async Task<TimeSeriesClusterScenario> PrepareSeriesAsync(ClusterFixture fixture,
        CancellationToken cancellationToken)
    {
        var scenario = await TimeSeriesClusterScenario.CreateAsync(fixture, cancellationToken);
        await scenario.AppendAsync(fixture, TimeSeriesClusterScenario.Series,
            TimeSeriesClusterScenario.Samples(TimeSeriesClusterScenario.Data(InitialSampleId, TimeSeriesClusterScenario.Start, 3)),
            TimeSeriesClusterScenario.PublicTags, cancellationToken);
        return scenario;
    }

    private static async Task<int> SelectSiloIndexAsync(KeyLoadClient[] clients,
        CancellationToken cancellationToken)
    {
        var statuses = (await Task.WhenAll(clients.Select(client => client.StatusAsync(cancellationToken))))
            .Select(ClusterReplicationTestSupport.Success).ToArray();
        await Assert.That(statuses.All(status => status.SiloCount == NodeCount)).IsTrue();
        await Assert.That(statuses.All(status => status.RoutingReady && status.ReplicaCount == NodeCount)).IsTrue();
        await Assert.That(statuses.Select(status => status.NodeId).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(NodeCount);
        return NodeCount - 1;
    }

    private static async Task VerifySeriesAsync(KeyLoadClient client, TimeSeriesClusterScenario scenario,
        CancellationToken cancellationToken)
    {
        var latest = await client.ReadLatestSampleAsync(scenario.Latest(), cancellationToken);
        await Assert.That(latest.IsSuccess).IsTrue().Because(string.Format(CultureInfo.InvariantCulture,
            LatestFailureFormat, latest.Problem?.ErrorCode ?? MissingProblemCode, latest.Problem?.Detail ?? MissingSafeDetail));
        await Assert.That(latest.Value!.Sample?.Sample.EventId).IsEqualTo(LateSampleId);
        await Assert.That(latest.Value.Sample?.Sample.Value).IsEqualTo(-7d);
        var request = scenario.Windows(TimeSeriesClusterScenario.Start,
            TimeSeriesClusterScenario.Start.AddMinutes(2), TimeSeriesClusterScenario.Minute);
        var windows = await client.AggregateSampleWindowsAsync(request, cancellationToken);
        await Assert.That(windows.IsSuccess).IsTrue().Because(string.Format(CultureInfo.InvariantCulture,
            WindowsFailureFormat, windows.Problem?.ErrorCode ?? MissingProblemCode, windows.Problem?.Detail ?? MissingSafeDetail));
        await Assert.That(windows.Value!.Windows.Length).IsEqualTo(2);
        await Assert.That(windows.Value.Windows[0].Aggregate.Count).IsEqualTo(1L);
        await Assert.That(windows.Value.Windows[0].Aggregate.Sum).IsEqualTo(3d);
        await Assert.That(windows.Value.Windows[1].Aggregate.Count).IsEqualTo(1L);
        await Assert.That(windows.Value.Windows[1].Aggregate.Sum).IsEqualTo(-7d);
    }

    private async Task CaptureDiagnosticsAsync(Exception failure)
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

    private async Task RestoreSiloAsync(string node, KeyLoadClient[] clients, Exception failure)
    {
        using var recovery = new CancellationTokenSource(ReadinessLifetime, TimeProvider.System);
        try
        {
            await fixture.RestartServerAsync(node, recovery.Token);
            var index = NodeIndex(node);
            await ClusterReplicationTestSupport.EventuallyAsync(async () =>
            {
                var status = await clients[index].StatusAsync(recovery.Token);
                return status.IsSuccess && status.Value!.RoutingReady;
            }, recovery.Token);
        }
        catch (Exception recoveryFailure) when (ClusterReplicationTestSupport.IsNonFatalCleanupFailure(recoveryFailure))
        {
            failure.Data[RestartFailureKey] = recoveryFailure;
        }
    }

    private static int NodeIndex(string node) => int.Parse(node.AsSpan("node".Length),
        System.Globalization.CultureInfo.InvariantCulture) - FirstNode;

    private static string NodeName(int number) => "node" + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
