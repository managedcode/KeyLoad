using System.Globalization;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

/// <summary>AC-SERIES-012: a real follower restart retains quorum-committed series data on RF3.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class TimeSeriesRf3ReplicaRestartTests(ClusterFixture fixture)
{
    private const string FailureScenario = "series-follower-restart";
    private const string RestartFailureKey = "KeyLoad.TimeSeriesFollowerRestartFailure";
    private const string InitialSampleId = "initial";
    private const string LateSampleId = "late";
    private const string LatestFailureMessage = "Latest-sample read failed: {0}; {1}.";
    private const string WindowsFailureMessage = "Window aggregate read failed: {0}; {1}.";
    private const string MissingProblemCode = "MissingProblem";
    private const string MissingSafeDetail = "No safe detail returned.";
    private const string MissingLeaderUri = "The initialized RF3 node status omitted its current leader URI.";
    private const int NodeCount = TimeSeriesRf3Scenario.NodeCount;
    private const int FirstNode = TimeSeriesRf3Scenario.FirstNode;
    private const int ReadinessSeconds = 45;
    private static readonly CompositeFormat LatestFailureFormat = CompositeFormat.Parse(LatestFailureMessage);
    private static readonly CompositeFormat WindowsFailureFormat = CompositeFormat.Parse(WindowsFailureMessage);
    private static readonly TimeSpan OriginalTestLifetime = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ReadinessLifetime = TimeSpan.FromSeconds(ReadinessSeconds);

    [Test]
    public async Task AcSeries012FollowerRestartRetainsLatestAndWindowValuesOnEveryReplica()
    {
        using var deadline = new CancellationTokenSource(OriginalTestLifetime);
        var clients = Enumerable.Range(FirstNode, NodeCount).Select(number => fixture.Client(NodeName(number))).ToArray();
        string? stoppedNode = null;
        var restarted = false;
        try
        {
            var scenario = await PrepareSeriesAsync(fixture, deadline.Token);
            var followerIndex = await SelectFollowerIndexAsync(clients, deadline.Token);
            var followerNode = NodeName(followerIndex + FirstNode);
            await fixture.KillContainerAsync(followerNode, FailureScenario, deadline.Token);
            stoppedNode = followerNode;

            var survivorIndex = Enumerable.Range(0, NodeCount).First(index => index != followerIndex);
            var late = TimeSeriesRf3Scenario.Data(LateSampleId, TimeSeriesRf3Scenario.Start.AddMinutes(1), -7);
            var command = new CommandRequest(Guid.NewGuid(), scenario.Partition,
                [new AppendSamples(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series, [late],
                    TimeSeriesRf3Scenario.PublicTags)]);
            var committed = await ClusterReplicationTestSupport.RetryDuringElectionAsync(
                () => clients[survivorIndex].CommitAsync(command, deadline.Token), deadline.Token);
            await Assert.That(committed.IsSuccess).IsTrue();

            foreach (var index in Enumerable.Range(0, NodeCount).Where(index => index != followerIndex))
            {
                await VerifySeriesAsync(clients[index], scenario, deadline.Token);
            }

            await fixture.RestartContainerAsync(stoppedNode, deadline.Token);
            restarted = true;
            await ClusterReplicationTestSupport.EventuallyAsync(async () =>
            {
                var status = await clients[followerIndex].StatusAsync(deadline.Token);
                return status.IsSuccess && status.Value!.RoutingReady;
            }, deadline.Token);
            await Assert.That(Directory.Exists(Path.Combine(fixture.Root, stoppedNode))).IsTrue();
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
                await RestoreFollowerAsync(stoppedNode, clients, failure);
            }

            throw;
        }
    }

    private static async Task<TimeSeriesRf3Scenario> PrepareSeriesAsync(ClusterFixture fixture,
        CancellationToken cancellationToken)
    {
        var scenario = await TimeSeriesRf3Scenario.CreateAsync(fixture, cancellationToken);
        await scenario.AppendAsync(fixture, TimeSeriesRf3Scenario.Series,
            TimeSeriesRf3Scenario.Samples(TimeSeriesRf3Scenario.Data(InitialSampleId, TimeSeriesRf3Scenario.Start, 3)),
            TimeSeriesRf3Scenario.PublicTags, cancellationToken);
        return scenario;
    }

    private static async Task<int> SelectFollowerIndexAsync(KeyLoadClient[] clients,
        CancellationToken cancellationToken)
    {
        var statuses = (await Task.WhenAll(clients.Select(client => client.StatusAsync(cancellationToken))))
            .Select(ClusterReplicationTestSupport.Success).ToArray();
        await Assert.That(statuses.All(status => status.Voters == NodeCount)).IsTrue();
        var leaderUri = new Uri(statuses[0].Leader ?? throw new InvalidOperationException(MissingLeaderUri));
        await Assert.That(statuses.All(status => string.Equals(status.Leader, statuses[0].Leader,
            StringComparison.Ordinal))).IsTrue();
        var leaderIndex = Enumerable.Range(0, NodeCount).Single(index =>
            string.Equals(leaderUri.Host, NodeName(index + FirstNode), StringComparison.Ordinal));
        return Enumerable.Range(0, NodeCount).First(index => index != leaderIndex);
    }

    private static async Task VerifySeriesAsync(KeyLoadClient client, TimeSeriesRf3Scenario scenario,
        CancellationToken cancellationToken)
    {
        var latest = await client.ReadLatestSampleAsync(scenario.Latest(), cancellationToken);
        await Assert.That(latest.IsSuccess).IsTrue().Because(string.Format(CultureInfo.InvariantCulture,
            LatestFailureFormat, latest.Problem?.ErrorCode ?? MissingProblemCode, latest.Problem?.Detail ?? MissingSafeDetail));
        await Assert.That(latest.Value!.Sample?.Sample.EventId).IsEqualTo(LateSampleId);
        await Assert.That(latest.Value.Sample?.Sample.Value).IsEqualTo(-7d);
        var request = scenario.Windows(TimeSeriesRf3Scenario.Start,
            TimeSeriesRf3Scenario.Start.AddMinutes(2), TimeSeriesRf3Scenario.Minute);
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
            failure.Data[ClusterReplicationTestSupport.Rf3DiagnosticsFailureKey] = diagnosticFailure;
        }
    }

    private async Task RestoreFollowerAsync(string node, KeyLoadClient[] clients, Exception failure)
    {
        using var recovery = new CancellationTokenSource(ReadinessLifetime);
        try
        {
            await fixture.RestartContainerAsync(node, recovery.Token);
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
