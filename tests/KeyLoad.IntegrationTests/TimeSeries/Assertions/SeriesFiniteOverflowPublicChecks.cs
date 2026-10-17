using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SeriesFiniteOverflowPublicChecks
{
    internal static async Task CurrentAsync(RequestCqrsRf3Callers administrator,
        SeriesFiniteOverflowPublicState state, CancellationToken token)
    {
        var range = SeriesFiniteOverflowPublicExpected.Range(state);
        var rows = SeriesFiniteOverflowPublicExpected.Rows(state.Purged);
        await SqlRf3Protocol.EqualAsync(rows,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadSamplesAsync(range, token)));
        await SqlRf3Protocol.EqualAsync(rows, (await McpCallerAssertions.SuccessAsync<SampleRecord[]>(
            await administrator.Mcp.CallAsync(McpCallerTools.SeriesRead, range, token))).Value);
        var latest = SeriesFiniteOverflowPublicExpected.LatestRequest(state);
        await SqlRf3Protocol.EqualAsync(SeriesFiniteOverflowPublicExpected.Latest(),
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadLatestSampleAsync(latest, token)));
        await SqlRf3Protocol.EqualAsync(SeriesFiniteOverflowPublicExpected.Latest(),
            (await McpCallerAssertions.SuccessAsync<LatestSampleResult>(
                await administrator.Mcp.CallAsync(McpCallerTools.SeriesLatest, latest, token))).Value);
        var retention = SeriesFiniteOverflowPublicExpected.RetentionRequest(state);
        var progress = SeriesFiniteOverflowPublicExpected.Retention(state.Purged);
        await SqlRf3Protocol.EqualAsync(progress,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadSampleRetentionAsync(retention, token)));
        await SqlRf3Protocol.EqualAsync(progress, (await McpCallerAssertions.SuccessAsync<SampleRetentionStatus>(
            await administrator.Mcp.CallAsync(McpCallerTools.SeriesRetention, retention, token))).Value);
        await SqlRf3Protocol.EqualAsync(state.CurrentOutbox,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token)));
        await SqlRf3Protocol.EqualAsync(state.CurrentOutbox, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await administrator.Mcp.CallAsync(McpCallerTools.OutboxStatus,
                new GetOutboxStatusRequest(state.Scenario.Partition), token))).Value);
    }

    internal static async Task HealthyAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, SeriesFiniteOverflowPublicState state, CancellationToken token)
    {
        var range = SeriesFiniteOverflowPublicExpected.Range(state);
        await SeriesFiniteOverflowPublicObservation.ReadAsync(administrator, reader, state,
            McpCallerTools.SeriesRead, range, SeriesFiniteOverflowPublicExpected.Rows(state.Purged),
            ct => reader.Sdk.ReadSamplesAsync(range, ct), token);
        var latest = SeriesFiniteOverflowPublicExpected.LatestRequest(state);
        await SeriesFiniteOverflowPublicObservation.ReadAsync(administrator, reader, state,
            McpCallerTools.SeriesLatest, latest, SeriesFiniteOverflowPublicExpected.Latest(),
            ct => reader.Sdk.ReadLatestSampleAsync(latest, ct), token);
        var retention = SeriesFiniteOverflowPublicExpected.RetentionRequest(state);
        await SeriesFiniteOverflowPublicObservation.ReadAsync(administrator, reader, state,
            McpCallerTools.SeriesRetention, retention, SeriesFiniteOverflowPublicExpected.Retention(state.Purged),
            ct => reader.Sdk.ReadSampleRetentionAsync(retention, ct), token);
        await AggregateAsync(administrator, reader, state,
            SeriesFiniteOverflowPublicExpected.Narrow(state), token);
        if (state.Purged)
        {
            await AggregateAsync(administrator, reader, state,
                SeriesFiniteOverflowPublicExpected.Whole(state), token);
        }
        await SeriesFiniteOverflowPublicWindowChecks.HealthyAsync(administrator, reader, state, token);
    }

    private static Task AggregateAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, SeriesFiniteOverflowPublicState state,
        AggregateSamplesRequest request, CancellationToken token)
        => SeriesFiniteOverflowPublicObservation.ReadAsync(administrator, reader, state,
            McpCallerTools.SeriesAggregate, request, SeriesFiniteOverflowPublicExpected.Finite(),
            ct => reader.Sdk.AggregateSamplesAsync(request, ct), token);

    internal static async Task OverflowAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, SeriesFiniteOverflowPublicState state, CancellationToken token)
    {
        await Assert.That(state.Purged).IsFalse();
        var request = SeriesFiniteOverflowPublicExpected.Whole(state);
        var result = await reader.Sdk.AggregateSamplesAsync(request, token);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Problem).IsNotNull();
        var expected = Errors.Problem(ErrorCode.Validation,
            SeriesFiniteOverflowPublicExpected.OverflowDetail);
        await SqlRf3Protocol.EqualAsync(expected, result.Problem!);
        await SeriesFiniteOverflowPublicChecks.CurrentAsync(administrator, state, token);
        await AggregateAsync(administrator, reader, state,
            SeriesFiniteOverflowPublicExpected.Narrow(state), token);
        await SeriesFiniteOverflowPublicObservation.RefusalAsync(reader,
            McpCallerTools.SeriesAggregate, request, token);
        await SeriesFiniteOverflowPublicChecks.CurrentAsync(administrator, state, token);
        await AggregateAsync(administrator, reader, state,
            SeriesFiniteOverflowPublicExpected.Narrow(state), token);
        await SeriesFiniteOverflowPublicWindowChecks.OverflowAsync(administrator, reader, state, token);
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, SeriesFiniteOverflowPublicState state, CancellationToken token)
    {
        await OriginalAsync(administrator, reader, state, state.Seed, state.SeedReceipt, token);
        if (state.ExpiryReceipt is { } expiry)
        {
            await OriginalAsync(administrator, reader, state, state.Expiry, expiry, token);
        }
    }

    private static async Task OriginalAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, SeriesFiniteOverflowPublicState state,
        CommandRequest command, CommitReceipt receipt, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(receipt,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token)));
        await CurrentAsync(administrator, state, token);
        await HealthyAsync(administrator, reader, state, token);
        await SqlRf3Protocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value);
        await CurrentAsync(administrator, state, token);
        await HealthyAsync(administrator, reader, state, token);
    }
}
