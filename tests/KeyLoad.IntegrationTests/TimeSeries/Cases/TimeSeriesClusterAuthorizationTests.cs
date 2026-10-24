using System.Net;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.TimeSeries;

/// <summary>AC-SERIES-011: persisted grants, output budgets and failure recovery cross real caller transports.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class TimeSeriesClusterAuthorizationTests(ClusterFixture fixture)
{
    private const string FirstSampleId = "one";
    private const string SecondSampleId = "two";
    private const string LargeFirstSampleId = "large-a";
    private const string LargeSecondSampleId = "large-b";

    [Test]
    public async Task AcSeries011EmptyResultsProjectedTagsRevocationAndBudgetFailuresAreConsistent()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await TimeSeriesClusterScenario.CreateAsync(fixture, deadline.Token);
        await scenario.AppendAsync(fixture, TimeSeriesClusterScenario.Series,
            TimeSeriesClusterScenario.Samples(TimeSeriesClusterScenario.Data(FirstSampleId, TimeSeriesClusterScenario.Start, 1),
                TimeSeriesClusterScenario.Data(SecondSampleId, TimeSeriesClusterScenario.Start.AddTicks(1), 2)),
            TimeSeriesClusterScenario.PrivateTags, deadline.Token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            TimeSeriesClusterScenario.Set, Capability.SeriesRead, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, TimeSeriesClusterScenario.Node1);
        var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, TimeSeriesClusterScenario.Node2,
            identity.Secret, deadline.Token);

        await scenario.AppendAsync(fixture, TimeSeriesClusterScenario.OverflowSeries,
            TimeSeriesClusterScenario.Samples(
                TimeSeriesClusterScenario.Data(LargeFirstSampleId, TimeSeriesClusterScenario.Start, double.MaxValue),
                TimeSeriesClusterScenario.Data(LargeSecondSampleId, TimeSeriesClusterScenario.Start.AddTicks(1), double.MaxValue)),
            TimeSeriesClusterScenario.PublicTags, deadline.Token);
        await VerifyAuthorizedEmptyResultsAsync(sdk, mcp, scenario, deadline.Token);
        var sdkLatest = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadLatestSampleAsync(scenario.Latest(), deadline.Token));
        var mcpLatest = await McpCallerAssertions.SuccessAsync<LatestSampleResult>(
            await mcp.CallAsync(McpCallerTools.SeriesLatest, scenario.Latest(), deadline.Token));
        await Assert.That(sdkLatest.Sample?.TagsJson).IsEqualTo(TimeSeriesClusterScenario.ProjectedTags);
        await Assert.That(sdkLatest.Sample?.TagsJson.Contains("private-marker", StringComparison.Ordinal)).IsFalse();
        await Assert.That(JsonDefaults.Serialize(sdkLatest).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(mcpLatest.Value))).IsTrue();

        await VerifyMalformedRequestsAsync(sdk, mcp, scenario, deadline.Token);
        await VerifyBudgetFailureAndHealthyFollowupAsync(sdk, mcp, scenario, deadline.Token);
        await VerifyOverflowAndHealthyFollowupAsync(sdk, mcp, scenario, deadline.Token);
        await VerifyPreCancelledSdkReadsAsync(sdk, scenario);
        var healthy = await McpCallerAssertions.SdkSuccessAsync(await sdk.AggregateSamplesAsync(
            scenario.Aggregate(TimeSeriesClusterScenario.Start, maxSamples: TimeSeriesClusterScenario.TwoSampleCap),
            deadline.Token));
        await Assert.That(healthy.Count).IsEqualTo(2L);

        var revoked = identity.Principal with
        {
            Revoked = true,
            PolicyEpoch = identity.Principal.PolicyEpoch + 1
        };
        await McpCallerAssertions.SdkSuccessAsync(await new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution())
            .ConfigurePrincipalAsync(Guid.NewGuid(), revoked, deadline.Token));
        await VerifyRevokedOperationsAsync(sdk, mcp, scenario, identity.Secret, deadline.Token);
    }

    private static async Task VerifyPreCancelledSdkReadsAsync(KeyLoadClient sdk, TimeSeriesClusterScenario scenario)
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await AssertSdkFailureAsync(await sdk.ReadLatestSampleAsync(scenario.Latest(), cancelled.Token), ErrorCode.Cancelled);
        await AssertSdkFailureAsync(await sdk.AggregateSamplesAsync(
            scenario.Aggregate(TimeSeriesClusterScenario.Start), cancelled.Token), ErrorCode.Cancelled);
        await AssertSdkFailureAsync(await sdk.AggregateSampleWindowsAsync(
            scenario.Windows(TimeSeriesClusterScenario.Start, TimeSeriesClusterScenario.Start.Add(TimeSeriesClusterScenario.Minute),
                TimeSeriesClusterScenario.Minute), cancelled.Token), ErrorCode.Cancelled);
    }

    private static async Task VerifyAuthorizedEmptyResultsAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        TimeSeriesClusterScenario scenario, CancellationToken cancellationToken)
    {
        var emptyLatest = scenario.Latest(TimeSeriesClusterScenario.EmptySeries);
        var sdkLatest = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadLatestSampleAsync(emptyLatest, cancellationToken));
        var mcpLatest = await McpCallerAssertions.SuccessAsync<LatestSampleResult>(
            await mcp.CallAsync(McpCallerTools.SeriesLatest, emptyLatest, cancellationToken));
        await Assert.That(sdkLatest.Sample).IsNull();
        await Assert.That(mcpLatest.Value.Sample).IsNull();
        var emptyAggregate = scenario.Aggregate(TimeSeriesClusterScenario.Start, TimeSeriesClusterScenario.Start);
        var aggregate = await McpCallerAssertions.SdkSuccessAsync(await sdk.AggregateSamplesAsync(emptyAggregate, cancellationToken));
        var mcpAggregate = await McpCallerAssertions.SuccessAsync<SampleAggregate>(
            await mcp.CallAsync(McpCallerTools.SeriesAggregate, emptyAggregate, cancellationToken));
        await Assert.That(aggregate).IsEqualTo(new SampleAggregate(0, 0, null, null, null));
        await Assert.That(mcpAggregate.Value).IsEqualTo(aggregate);
        var emptyWindows = scenario.Windows(TimeSeriesClusterScenario.Start, TimeSeriesClusterScenario.Start,
            TimeSeriesClusterScenario.Minute);
        var windows = await McpCallerAssertions.SdkSuccessAsync(await sdk.AggregateSampleWindowsAsync(emptyWindows, cancellationToken));
        var mcpWindows = await McpCallerAssertions.SuccessAsync<SampleAggregateWindowsResult>(
            await mcp.CallAsync(McpCallerTools.SeriesWindows, emptyWindows, cancellationToken));
        await Assert.That(windows.Windows.IsEmpty).IsTrue();
        await Assert.That(mcpWindows.Value.Windows).IsEmpty();
    }

    private static async Task VerifyMalformedRequestsAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        TimeSeriesClusterScenario scenario, CancellationToken cancellationToken)
    {
        var inverted = scenario.Aggregate(TimeSeriesClusterScenario.Start.AddTicks(1), TimeSeriesClusterScenario.Start);
        await AssertSdkFailureAsync(await sdk.AggregateSamplesAsync(inverted, cancellationToken), ErrorCode.Validation);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.SeriesAggregate,
            inverted, cancellationToken), ErrorCode.Validation, dispatched: true);
        var invalidWidth = scenario.Windows(TimeSeriesClusterScenario.Start,
            TimeSeriesClusterScenario.Start.Add(TimeSeriesClusterScenario.Minute), TimeSpan.Zero);
        await AssertSdkFailureAsync(await sdk.AggregateSampleWindowsAsync(invalidWidth, cancellationToken), ErrorCode.Validation);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.SeriesWindows,
            invalidWidth, cancellationToken), ErrorCode.Validation, dispatched: true);
    }

    private static async Task VerifyBudgetFailureAndHealthyFollowupAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        TimeSeriesClusterScenario scenario, CancellationToken cancellationToken)
    {
        var capped = scenario.Aggregate(TimeSeriesClusterScenario.Start,
            maxSamples: TimeSeriesClusterScenario.OneSampleCap);
        await AssertSdkFailureAsync(await sdk.AggregateSamplesAsync(capped, cancellationToken), ErrorCode.BudgetExceeded);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.SeriesAggregate,
            capped, cancellationToken), ErrorCode.BudgetExceeded, dispatched: true);
        var healthy = await McpCallerAssertions.SdkSuccessAsync(await sdk.AggregateSamplesAsync(
            scenario.Aggregate(TimeSeriesClusterScenario.Start, maxSamples: TimeSeriesClusterScenario.TwoSampleCap),
            cancellationToken));
        await Assert.That(healthy.Count).IsEqualTo(2L);
    }

    private static async Task VerifyOverflowAndHealthyFollowupAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        TimeSeriesClusterScenario scenario, CancellationToken cancellationToken)
    {
        var request = new AggregateSamplesRequest(scenario.Partition, TimeSeriesClusterScenario.Set,
            TimeSeriesClusterScenario.OverflowSeries, TimeSeriesClusterScenario.Start);
        await AssertSdkFailureAsync(await sdk.AggregateSamplesAsync(request, cancellationToken), ErrorCode.Validation);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.SeriesAggregate,
            request, cancellationToken), ErrorCode.Validation, dispatched: true);
        var persisted = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadLatestSampleAsync(
            new(scenario.Partition, TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.OverflowSeries), cancellationToken));
        await Assert.That(persisted.Sample?.Sample.EventId).IsEqualTo(LargeSecondSampleId);
        var healthy = await McpCallerAssertions.SdkSuccessAsync(await sdk.AggregateSamplesAsync(
            scenario.Aggregate(TimeSeriesClusterScenario.Start, maxSamples: TimeSeriesClusterScenario.TwoSampleCap),
            cancellationToken));
        await Assert.That(healthy.Count).IsEqualTo(2L);
    }

    private async Task VerifyRevokedOperationsAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        TimeSeriesClusterScenario scenario, string secret, CancellationToken cancellationToken)
    {
        var aggregate = scenario.Aggregate(TimeSeriesClusterScenario.Start);
        var windows = scenario.Windows(TimeSeriesClusterScenario.Start,
            TimeSeriesClusterScenario.Start.Add(TimeSeriesClusterScenario.OneTick), TimeSeriesClusterScenario.Minute);
        await AssertSdkFailureAsync(await sdk.ReadLatestSampleAsync(scenario.Latest(), cancellationToken), ErrorCode.Unauthenticated);
        await AssertSdkFailureAsync(await sdk.AggregateSamplesAsync(aggregate, cancellationToken), ErrorCode.Unauthenticated);
        await AssertSdkFailureAsync(await sdk.AggregateSampleWindowsAsync(windows, cancellationToken), ErrorCode.Unauthenticated);
        await AssertMcpUnauthorizedAsync(mcp, McpCallerTools.SeriesLatest, scenario.Latest(), secret, cancellationToken);
        await AssertMcpUnauthorizedAsync(mcp, McpCallerTools.SeriesAggregate, aggregate, secret, cancellationToken);
        await AssertMcpUnauthorizedAsync(mcp, McpCallerTools.SeriesWindows, windows, secret, cancellationToken);
        await McpUnauthorizedProbe.VerifyAsync(fixture, TimeSeriesClusterScenario.Node2, secret, cancellationToken);
    }

    private static async Task AssertMcpUnauthorizedAsync<T>(McpOfficialClient mcp, string tool, T request,
        string secret, CancellationToken cancellationToken)
    {
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => mcp.CallAsync(tool, request, cancellationToken));
        await Assert.That(error!.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(error.Message.Contains(secret, StringComparison.Ordinal)).IsFalse();
    }

    private static async Task AssertSdkFailureAsync<T>(ManagedCode.Communication.Result<T> result,
        ErrorCode expected)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(expected.ToString());
    }
}
