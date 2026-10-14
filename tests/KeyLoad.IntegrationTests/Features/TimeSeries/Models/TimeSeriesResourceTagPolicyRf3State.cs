using KeyLoad.IntegrationTests.Features.ClientApi;
namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal sealed class TimeSeriesResourceTagPolicyRf3State(TimeSeriesRf3Scenario scenario,
    McpPersistedIdentity reader, ResourceDefinition definition, AtomicPartitionPlacementResolution placement,
    CommandRequest append, CommitReceipt receipt, Guid createId)
{
    internal TimeSeriesRf3Scenario Scenario { get; } = scenario;
    internal McpPersistedIdentity Reader { get; } = reader;
    internal ResourceDefinition Current { get; set; } = definition;
    internal AtomicPartitionPlacementResolution Placement { get; } = placement;
    internal CommandRequest Append { get; } = append;
    internal CommitReceipt Receipt { get; } = receipt;
    internal List<(Guid Id, ConfigureResourceRequest Request, ResourceDefinition Result)> Configurations
    {
        get;
    } =
        [(createId, new(scenario.Partition.TenantId, scenario.Partition.DatabaseId, definition), definition)];
    internal Guid StaleId { get; } = Guid.NewGuid();
    internal ConfigureResourceRequest? Stale
    {
        get; set;
    }
    internal OutboxStatus Outbox { get; set; } = null!;

    internal ReadSamplesRequest Range() => new(Scenario.Partition, TimeSeriesRf3Scenario.Set,
        TimeSeriesRf3Scenario.Series, TimeSeriesRf3Scenario.Start, TimeSeriesRf3Scenario.Start.AddTicks(1), 2);

    internal AggregateSamplesRequest Aggregate() => Scenario.Aggregate(TimeSeriesRf3Scenario.Start,
        TimeSeriesRf3Scenario.Start.AddTicks(2), 2);
}
