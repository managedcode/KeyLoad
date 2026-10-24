using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.TimeSeries;

internal static class SeriesFiniteOverflowPublicChecks
{
    internal static async Task CurrentAsync(RequestCqrsClusterCallers administrator,
        SeriesFiniteOverflowPublicState state, CancellationToken token)
    {
        var range = SeriesFiniteOverflowPublicExpected.Range(state);
        var rows = SeriesFiniteOverflowPublicExpected.Rows(state.Purged);
        await SqlClusterProtocol.EqualAsync(rows,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadSamplesAsync(range, token)));
        await SqlClusterProtocol.EqualAsync(rows, (await McpCallerAssertions.SuccessAsync<SampleRecord[]>(
            await administrator.Mcp.CallAsync(McpCallerTools.SeriesRead, range, token))).Value);
        var latest = SeriesFiniteOverflowPublicExpected.LatestRequest(state);
        await SqlClusterProtocol.EqualAsync(SeriesFiniteOverflowPublicExpected.Latest(),
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadLatestSampleAsync(latest, token)));
        await SqlClusterProtocol.EqualAsync(SeriesFiniteOverflowPublicExpected.Latest(),
            (await McpCallerAssertions.SuccessAsync<LatestSampleResult>(
                await administrator.Mcp.CallAsync(McpCallerTools.SeriesLatest, latest, token))).Value);
        var retention = SeriesFiniteOverflowPublicExpected.RetentionRequest(state);
        var progress = SeriesFiniteOverflowPublicExpected.Retention(state.Purged);
        await SqlClusterProtocol.EqualAsync(progress,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadSampleRetentionAsync(retention, token)));
        await SqlClusterProtocol.EqualAsync(progress, (await McpCallerAssertions.SuccessAsync<SampleRetentionStatus>(
            await administrator.Mcp.CallAsync(McpCallerTools.SeriesRetention, retention, token))).Value);
        await SqlClusterProtocol.EqualAsync(state.CurrentOutbox,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token)));
        await SqlClusterProtocol.EqualAsync(state.CurrentOutbox, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await administrator.Mcp.CallAsync(McpCallerTools.OutboxStatus,
                new GetOutboxStatusRequest(state.Scenario.Partition), token))).Value);
    }

    internal static async Task HealthyAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, SeriesFiniteOverflowPublicState state, CancellationToken token)
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

    private static Task AggregateAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, SeriesFiniteOverflowPublicState state,
        AggregateSamplesRequest request, CancellationToken token)
        => SeriesFiniteOverflowPublicObservation.ReadAsync(administrator, reader, state,
            McpCallerTools.SeriesAggregate, request, SeriesFiniteOverflowPublicExpected.Finite(),
            ct => reader.Sdk.AggregateSamplesAsync(request, ct), token);

    internal static async Task OverflowAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, SeriesFiniteOverflowPublicState state, CancellationToken token)
    {
        await Assert.That(state.Purged).IsFalse();
        var request = SeriesFiniteOverflowPublicExpected.Whole(state);
        var result = await reader.Sdk.AggregateSamplesAsync(request, token);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Problem).IsNotNull();
        var expected = Errors.Problem(ErrorCode.Validation,
            SeriesFiniteOverflowPublicExpected.OverflowDetail);
        await SqlClusterProtocol.EqualAsync(expected, result.Problem!);
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

    internal static async Task ReplayAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, SeriesFiniteOverflowPublicState state, CancellationToken token)
    {
        await OriginalAsync(administrator, reader, state, state.Seed, state.SeedReceipt, token);
        if (state.ExpiryReceipt is { } expiry)
        {
            await OriginalAsync(administrator, reader, state, state.Expiry, expiry, token);
        }
    }

    private static async Task OriginalAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, SeriesFiniteOverflowPublicState state,
        CommandRequest command, CommitReceipt receipt, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(receipt,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token)));
        await CurrentAsync(administrator, state, token);
        await HealthyAsync(administrator, reader, state, token);
        await SqlClusterProtocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value);
        await CurrentAsync(administrator, state, token);
        await HealthyAsync(administrator, reader, state, token);
    }
}
