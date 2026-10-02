using System.Net;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

/// <summary>AC-SERIES-011: persisted grants, output budgets and failure recovery cross real caller transports.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class TimeSeriesRf3AuthorizationTests(ClusterFixture fixture)
{
    private const string FirstSampleId = "one";
    private const string SecondSampleId = "two";
    private const string LargeFirstSampleId = "large-a";
    private const string LargeSecondSampleId = "large-b";

    [Test]
    public async Task AcSeries011EmptyResultsProjectedTagsRevocationAndBudgetFailuresAreConsistent()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await TimeSeriesRf3Scenario.CreateAsync(fixture, deadline.Token);
        await scenario.AppendAsync(fixture, TimeSeriesRf3Scenario.Series,
            TimeSeriesRf3Scenario.Samples(TimeSeriesRf3Scenario.Data(FirstSampleId, TimeSeriesRf3Scenario.Start, 1),
                TimeSeriesRf3Scenario.Data(SecondSampleId, TimeSeriesRf3Scenario.Start.AddTicks(1), 2)),
            TimeSeriesRf3Scenario.PrivateTags, deadline.Token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            TimeSeriesRf3Scenario.Set, Capability.SeriesRead, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, TimeSeriesRf3Scenario.Node1);
        var sdk = new KeyLoadClient(http, identity.Secret);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, TimeSeriesRf3Scenario.Node2,
            identity.Secret, deadline.Token);

        await scenario.AppendAsync(fixture, TimeSeriesRf3Scenario.OverflowSeries,
            TimeSeriesRf3Scenario.Samples(
                TimeSeriesRf3Scenario.Data(LargeFirstSampleId, TimeSeriesRf3Scenario.Start, double.MaxValue),
                TimeSeriesRf3Scenario.Data(LargeSecondSampleId, TimeSeriesRf3Scenario.Start.AddTicks(1), double.MaxValue)),
            TimeSeriesRf3Scenario.PublicTags, deadline.Token);
        await VerifyAuthorizedEmptyResultsAsync(sdk, mcp, scenario, deadline.Token);
        var sdkLatest = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadLatestSampleAsync(scenario.Latest(), deadline.Token));
        var mcpLatest = await McpCallerAssertions.SuccessAsync<LatestSampleResult>(
            await mcp.CallAsync(McpCallerTools.SeriesLatest, scenario.Latest(), deadline.Token));
        await Assert.That(sdkLatest.Sample?.TagsJson).IsEqualTo(TimeSeriesRf3Scenario.ProjectedTags);
        await Assert.That(sdkLatest.Sample?.TagsJson.Contains("private-marker", StringComparison.Ordinal)).IsFalse();
        await Assert.That(JsonDefaults.Serialize(sdkLatest).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(mcpLatest.Value))).IsTrue();

        await VerifyMalformedRequestsAsync(sdk, mcp, scenario, deadline.Token);
        await VerifyBudgetFailureAndHealthyFollowupAsync(sdk, mcp, scenario, deadline.Token);
        await VerifyOverflowAndHealthyFollowupAsync(sdk, mcp, scenario, deadline.Token);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => sdk.ReadLatestSampleAsync(
            scenario.Latest(), cancelled.Token));
        await Assert.That((await McpCallerAssertions.SdkSuccessAsync(await sdk.AggregateSamplesAsync(
            scenario.Aggregate(TimeSeriesRf3Scenario.Start, maxSamples: TimeSeriesRf3Scenario.TwoSampleCap),
            deadline.Token))).Count).IsEqualTo(2L);

        var revoked = identity.Principal with { Revoked = true, PolicyEpoch = identity.Principal.PolicyEpoch + 1 };
        await McpCallerAssertions.SdkSuccessAsync(await new KeyLoadClient(http, fixture.AdminKey)
            .ConfigurePrincipalAsync(Guid.NewGuid(), revoked, deadline.Token));
        await VerifyRevokedOperationsAsync(sdk, mcp, scenario, identity.Secret, deadline.Token);
    }

    private static async Task VerifyAuthorizedEmptyResultsAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        TimeSeriesRf3Scenario scenario, CancellationToken cancellationToken)
    {
        var emptyLatest = scenario.Latest(TimeSeriesRf3Scenario.EmptySeries);
        var sdkLatest = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadLatestSampleAsync(emptyLatest, cancellationToken));
        var mcpLatest = await McpCallerAssertions.SuccessAsync<LatestSampleResult>(
            await mcp.CallAsync(McpCallerTools.SeriesLatest, emptyLatest, cancellationToken));
        await Assert.That(sdkLatest.Sample).IsNull();
        await Assert.That(mcpLatest.Value.Sample).IsNull();
        var emptyAggregate = scenario.Aggregate(TimeSeriesRf3Scenario.Start, TimeSeriesRf3Scenario.Start);
        var aggregate = await McpCallerAssertions.SdkSuccessAsync(await sdk.AggregateSamplesAsync(emptyAggregate, cancellationToken));
        var mcpAggregate = await McpCallerAssertions.SuccessAsync<SampleAggregate>(
            await mcp.CallAsync(McpCallerTools.SeriesAggregate, emptyAggregate, cancellationToken));
        await Assert.That(aggregate).IsEqualTo(new SampleAggregate(0, 0, null, null, null));
        await Assert.That(mcpAggregate.Value).IsEqualTo(aggregate);
        var emptyWindows = scenario.Windows(TimeSeriesRf3Scenario.Start, TimeSeriesRf3Scenario.Start,
            TimeSeriesRf3Scenario.Minute);
        var windows = await McpCallerAssertions.SdkSuccessAsync(await sdk.AggregateSampleWindowsAsync(emptyWindows, cancellationToken));
        var mcpWindows = await McpCallerAssertions.SuccessAsync<SampleAggregateWindowsResult>(
            await mcp.CallAsync(McpCallerTools.SeriesWindows, emptyWindows, cancellationToken));
        await Assert.That(windows.Windows.IsEmpty).IsTrue();
        await Assert.That(mcpWindows.Value.Windows).IsEmpty();
    }

    private static async Task VerifyMalformedRequestsAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        TimeSeriesRf3Scenario scenario, CancellationToken cancellationToken)
    {
        var inverted = scenario.Aggregate(TimeSeriesRf3Scenario.Start.AddTicks(1), TimeSeriesRf3Scenario.Start);
        await AssertSdkFailureAsync(await sdk.AggregateSamplesAsync(inverted, cancellationToken), ErrorCode.Validation);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.SeriesAggregate,
            inverted, cancellationToken), ErrorCode.Validation, dispatched: true);
        var invalidWidth = scenario.Windows(TimeSeriesRf3Scenario.Start,
            TimeSeriesRf3Scenario.Start.Add(TimeSeriesRf3Scenario.Minute), TimeSpan.Zero);
        await AssertSdkFailureAsync(await sdk.AggregateSampleWindowsAsync(invalidWidth, cancellationToken), ErrorCode.Validation);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.SeriesWindows,
            invalidWidth, cancellationToken), ErrorCode.Validation, dispatched: true);
    }

    private static async Task VerifyBudgetFailureAndHealthyFollowupAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        TimeSeriesRf3Scenario scenario, CancellationToken cancellationToken)
    {
        var capped = scenario.Aggregate(TimeSeriesRf3Scenario.Start,
            maxSamples: TimeSeriesRf3Scenario.OneSampleCap);
        await AssertSdkFailureAsync(await sdk.AggregateSamplesAsync(capped, cancellationToken), ErrorCode.BudgetExceeded);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.SeriesAggregate,
            capped, cancellationToken), ErrorCode.BudgetExceeded, dispatched: true);
        var healthy = await McpCallerAssertions.SdkSuccessAsync(await sdk.AggregateSamplesAsync(
            scenario.Aggregate(TimeSeriesRf3Scenario.Start, maxSamples: TimeSeriesRf3Scenario.TwoSampleCap),
            cancellationToken));
        await Assert.That(healthy.Count).IsEqualTo(2L);
    }

    private static async Task VerifyOverflowAndHealthyFollowupAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        TimeSeriesRf3Scenario scenario, CancellationToken cancellationToken)
    {
        var request = new AggregateSamplesRequest(scenario.Partition, TimeSeriesRf3Scenario.Set,
            TimeSeriesRf3Scenario.OverflowSeries, TimeSeriesRf3Scenario.Start);
        await AssertSdkFailureAsync(await sdk.AggregateSamplesAsync(request, cancellationToken), ErrorCode.Validation);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.SeriesAggregate,
            request, cancellationToken), ErrorCode.Validation, dispatched: true);
        var persisted = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadLatestSampleAsync(
            new(scenario.Partition, TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.OverflowSeries), cancellationToken));
        await Assert.That(persisted.Sample?.Sample.EventId).IsEqualTo(LargeSecondSampleId);
        var healthy = await McpCallerAssertions.SdkSuccessAsync(await sdk.AggregateSamplesAsync(
            scenario.Aggregate(TimeSeriesRf3Scenario.Start, maxSamples: TimeSeriesRf3Scenario.TwoSampleCap),
            cancellationToken));
        await Assert.That(healthy.Count).IsEqualTo(2L);
    }

    private async Task VerifyRevokedOperationsAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        TimeSeriesRf3Scenario scenario, string secret, CancellationToken cancellationToken)
    {
        var aggregate = scenario.Aggregate(TimeSeriesRf3Scenario.Start);
        var windows = scenario.Windows(TimeSeriesRf3Scenario.Start,
            TimeSeriesRf3Scenario.Start.Add(TimeSeriesRf3Scenario.OneTick), TimeSeriesRf3Scenario.Minute);
        await AssertSdkFailureAsync(await sdk.ReadLatestSampleAsync(scenario.Latest(), cancellationToken), ErrorCode.Unauthenticated);
        await AssertSdkFailureAsync(await sdk.AggregateSamplesAsync(aggregate, cancellationToken), ErrorCode.Unauthenticated);
        await AssertSdkFailureAsync(await sdk.AggregateSampleWindowsAsync(windows, cancellationToken), ErrorCode.Unauthenticated);
        await AssertMcpUnauthorizedAsync(mcp, McpCallerTools.SeriesLatest, scenario.Latest(), secret, cancellationToken);
        await AssertMcpUnauthorizedAsync(mcp, McpCallerTools.SeriesAggregate, aggregate, secret, cancellationToken);
        await AssertMcpUnauthorizedAsync(mcp, McpCallerTools.SeriesWindows, windows, secret, cancellationToken);
        await McpUnauthorizedProbe.VerifyAsync(fixture, TimeSeriesRf3Scenario.Node2, secret, cancellationToken);
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
