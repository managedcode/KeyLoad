using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.TimeSeries;

internal static class ScopedSampleIdentityPublicChecks
{
    private const string DetailProperty = "detail";
    internal static async Task CurrentAsync(RequestCqrsClusterCallers administrator,
        ScopedSampleIdentityPublicState state, CancellationToken token)
    {
        foreach (var second in new[] { false, true })
        {
            var record = ScopedSampleIdentityPublicExpected.Record(second);
            var range = ScopedSampleIdentityPublicExpected.Range(state, record.SeriesId);
            var rows = new[] { record };
            await SqlClusterProtocol.EqualAsync(rows, await McpCallerAssertions.SdkSuccessAsync(
                await administrator.Sdk.ReadSamplesAsync(range, token)));
            await SqlClusterProtocol.EqualAsync(rows, (await McpCallerAssertions.SuccessAsync<SampleRecord[]>(
                await administrator.Mcp.CallAsync(McpCallerTools.SeriesRead, range, token))).Value);
            var latest = new ReadLatestSampleRequest(state.Scenario.Partition, TimeSeriesClusterScenario.Set, record.SeriesId);
            var expected = new LatestSampleResult(record);
            await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
                await administrator.Sdk.ReadLatestSampleAsync(latest, token)));
            await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<LatestSampleResult>(
                await administrator.Mcp.CallAsync(McpCallerTools.SeriesLatest, latest, token))).Value);
            var retention = new ReadSampleRetentionRequest(state.Scenario.Partition, TimeSeriesClusterScenario.Set, record.SeriesId);
            var empty = new SampleRetentionStatus(null, 0L, false);
            await SqlClusterProtocol.EqualAsync(empty, await McpCallerAssertions.SdkSuccessAsync(
                await administrator.Sdk.ReadSampleRetentionAsync(retention, token)));
            await SqlClusterProtocol.EqualAsync(empty, (await McpCallerAssertions.SuccessAsync<SampleRetentionStatus>(
                await administrator.Mcp.CallAsync(McpCallerTools.SeriesRetention, retention, token))).Value);
        }
        await SqlClusterProtocol.EqualAsync(state.Outbox, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token)));
        await SqlClusterProtocol.EqualAsync(state.Outbox, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await administrator.Mcp.CallAsync(McpCallerTools.OutboxStatus,
                new GetOutboxStatusRequest(state.Scenario.Partition), token))).Value);
    }

    internal static async Task HealthyAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, ScopedSampleIdentityPublicState state, CancellationToken token)
    {
        foreach (var second in new[] { false, true })
        {
            var record = ScopedSampleIdentityPublicExpected.Record(second);
            var aggregate = ScopedSampleIdentityPublicExpected.Aggregate(state, record.SeriesId);
            await ScopedSampleIdentityPublicObservation.ReadAsync(administrator, reader, state,
                McpCallerTools.SeriesAggregate, aggregate, ScopedSampleIdentityPublicExpected.AggregateValue(second),
                ct => reader.Sdk.AggregateSamplesAsync(aggregate, ct), token);
        }
    }

    internal static async Task ConflictsAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, ScopedSampleIdentityPublicState state, CancellationToken token)
    {
        foreach (var command in new[] { state.ValueConflict, state.TagConflict })
        {
            var result = await administrator.Sdk.CommitAsync(command, token);
            await Assert.That(result.IsFailed).IsTrue();
            await Assert.That(result.Value).IsNull();
            await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.Conflict,
                ScopedSampleIdentityPublicExpected.ConflictDetail), result.Problem!);
            await CurrentAsync(administrator, state, token);
            await HealthyAsync(administrator, reader, state, token);
            var reply = await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token);
            await McpCallerAssertions.ErrorAsync(reply, ErrorCode.Conflict, dispatched: true);
            await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
                .GetProperty(DetailProperty).GetString()).IsEqualTo(ScopedSampleIdentityPublicExpected.McpConflictDetail);
            await CurrentAsync(administrator, state, token);
            await HealthyAsync(administrator, reader, state, token);
        }
    }
}
