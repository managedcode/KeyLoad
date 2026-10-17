using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class ScopedSampleIdentityPublicCallerReads
{
    internal static async Task ReadAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, ScopedSampleIdentityPublicState state, CancellationToken token)
    {
        foreach (var second in new[] { false, true })
        {
            var record = ScopedSampleIdentityPublicExpected.Record(second);
            var range = ScopedSampleIdentityPublicExpected.Range(state, record.SeriesId);
            await ScopedSampleIdentityPublicObservation.ReadAsync(administrator, reader, state,
                McpCallerTools.SeriesRead, range, new[] { record }, ct => reader.Sdk.ReadSamplesAsync(range, ct), token);
            var latest = new ReadLatestSampleRequest(state.Scenario.Partition, TimeSeriesRf3Scenario.Set, record.SeriesId);
            await ScopedSampleIdentityPublicObservation.ReadAsync(administrator, reader, state,
                McpCallerTools.SeriesLatest, latest, new LatestSampleResult(record),
                ct => reader.Sdk.ReadLatestSampleAsync(latest, ct), token);
            var retention = new ReadSampleRetentionRequest(state.Scenario.Partition, TimeSeriesRf3Scenario.Set, record.SeriesId);
            await ScopedSampleIdentityPublicObservation.ReadAsync(administrator, reader, state,
                McpCallerTools.SeriesRetention, retention, new SampleRetentionStatus(null, 0L, false),
                ct => reader.Sdk.ReadSampleRetentionAsync(retention, ct), token);
        }
    }
}
