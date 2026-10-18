using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SeriesRetentionAtomicPublicPreparation
{
    internal static async Task<SeriesRetentionAtomicPublicState> CreateAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers administrator, CancellationToken token)
    {
        var scenario = await TimeSeriesClusterScenario.CreateAsync(fixture, token);
        var reader = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            TimeSeriesClusterScenario.Set, Capability.SeriesRead, token);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
        return new(scenario, reader, outbox)
        {
            Seed = Command(scenario.Partition, SeriesRetentionAtomicPublicExpected.Known(),
                SeriesRetentionAtomicPublicExpected.Floor()),
            Expiry = new(Guid.NewGuid(), scenario.Partition,
                [new ExpireSamples(TimeSeriesClusterScenario.Set, SeriesRetentionAtomicPublicExpected.Series,
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
            [new AppendSamples(TimeSeriesClusterScenario.Set, SeriesRetentionAtomicPublicExpected.Series,
                [.. samples], SeriesRetentionAtomicPublicExpected.Tags)]);
}
