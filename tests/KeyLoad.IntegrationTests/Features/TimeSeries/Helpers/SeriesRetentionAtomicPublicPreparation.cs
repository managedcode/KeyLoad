using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SeriesRetentionAtomicPublicPreparation
{
    internal static async Task<SeriesRetentionAtomicPublicState> CreateAsync(ClusterFixture fixture,
        RequestCqrsRf3Callers administrator, CancellationToken token)
    {
        var scenario = await TimeSeriesRf3Scenario.CreateAsync(fixture, token);
        var reader = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            TimeSeriesRf3Scenario.Set, Capability.SeriesRead, token);
        var request = new AtomicPartitionPlacementReadRequest(1, scenario.Partition);
        var placement = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ReadAtomicPartitionPlacementAsync(request, token));
        await SqlRf3Protocol.EqualAsync(placement,
            (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
                await administrator.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, request, token))).Value);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
        return new(scenario, reader, placement, outbox)
        {
            Seed = Command(scenario.Partition, SeriesRetentionAtomicPublicExpected.Known(),
                SeriesRetentionAtomicPublicExpected.Floor()),
            Expiry = new(Guid.NewGuid(), scenario.Partition,
                [new ExpireSamples(TimeSeriesRf3Scenario.Set, SeriesRetentionAtomicPublicExpected.Series,
                    SeriesRetentionAtomicPublicExpected.FloorAt, SeriesRetentionAtomicPublicExpected.MaximumDeletes)]),
            EchoSdk = Command(scenario.Partition, SeriesRetentionAtomicPublicExpected.Known()),
            EchoMcp = Command(scenario.Partition, SeriesRetentionAtomicPublicExpected.Known()),
            ConflictSdk = Command(scenario.Partition, SeriesRetentionAtomicPublicExpected.ChangedKnown()),
            ConflictMcp = Command(scenario.Partition, SeriesRetentionAtomicPublicExpected.ChangedKnown()),
            MixedSdk = Command(scenario.Partition, SeriesRetentionAtomicPublicExpected.Fresh(),
                SeriesRetentionAtomicPublicExpected.NewExpired(false)),
            MixedMcp = Command(scenario.Partition, SeriesRetentionAtomicPublicExpected.Fresh(),
                SeriesRetentionAtomicPublicExpected.NewExpired(true)),
            Fresh = Command(scenario.Partition, SeriesRetentionAtomicPublicExpected.Fresh())
        };
    }

    private static CommandRequest Command(PartitionRef partition, params SampleData[] samples)
        => new(Guid.NewGuid(), partition,
            [new AppendSamples(TimeSeriesRf3Scenario.Set, SeriesRetentionAtomicPublicExpected.Series,
                [.. samples], SeriesRetentionAtomicPublicExpected.Tags)]);
}
