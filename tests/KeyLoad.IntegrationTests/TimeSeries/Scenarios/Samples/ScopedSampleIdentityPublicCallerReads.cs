using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.TimeSeries;

internal static class ScopedSampleIdentityPublicCallerReads
{
    internal static async Task ReadAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, ScopedSampleIdentityPublicState state, CancellationToken token)
    {
        foreach (var second in new[] { false, true })
        {
            var record = ScopedSampleIdentityPublicExpected.Record(second);
            var range = ScopedSampleIdentityPublicExpected.Range(state, record.SeriesId);
            await ScopedSampleIdentityPublicObservation.ReadAsync(administrator, reader, state,
                McpCallerTools.SeriesRead, range, new[] { record }, ct => reader.Sdk.ReadSamplesAsync(range, ct), token);
            var latest = new ReadLatestSampleRequest(state.Scenario.Partition, TimeSeriesClusterScenario.Set, record.SeriesId);
            await ScopedSampleIdentityPublicObservation.ReadAsync(administrator, reader, state,
                McpCallerTools.SeriesLatest, latest, new LatestSampleResult(record),
                ct => reader.Sdk.ReadLatestSampleAsync(latest, ct), token);
            var retention = new ReadSampleRetentionRequest(state.Scenario.Partition, TimeSeriesClusterScenario.Set, record.SeriesId);
            await ScopedSampleIdentityPublicObservation.ReadAsync(administrator, reader, state,
                McpCallerTools.SeriesRetention, retention, new SampleRetentionStatus(null, 0L, false),
                ct => reader.Sdk.ReadSampleRetentionAsync(retention, ct), token);
        }
    }
}
