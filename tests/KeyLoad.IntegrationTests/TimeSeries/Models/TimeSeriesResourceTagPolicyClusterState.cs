using KeyLoad.IntegrationTests.Features.ClientApi;
namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal sealed class TimeSeriesResourceTagPolicyClusterState(TimeSeriesClusterScenario scenario,
    McpPersistedIdentity reader, ResourceDefinition definition, CommandRequest append, CommitReceipt receipt, Guid createId, Guid windowId,
    List<(CommandRequest Request, CommitReceipt Receipt)> chunkCommands)
{
    internal TimeSeriesClusterScenario Scenario { get; } = scenario;
    internal McpPersistedIdentity Reader { get; } = reader;
    internal ResourceDefinition Current { get; set; } = definition;
    internal CommandRequest Append { get; } = append;
    internal CommitReceipt Receipt { get; } = receipt;
    internal List<(Guid Id, ConfigureResourceRequest Request, ResourceDefinition Result)> Configurations
    {
        get;
    } =
        [(createId, new(scenario.Partition.TenantId, scenario.Partition.DatabaseId, definition), definition)];
    internal Guid WindowId { get; } = windowId;
    internal List<(CommandRequest Request, CommitReceipt Receipt)> ChunkCommands { get; } = chunkCommands;
    internal Guid StaleId { get; } = Guid.NewGuid();
    internal ConfigureResourceRequest? Stale
    {
        get; set;
    }
    internal OutboxStatus Outbox { get; set; } = null!;

    internal ReadSamplesRequest Range() => new(Scenario.Partition, TimeSeriesClusterScenario.Set,
        TimeSeriesClusterScenario.Series, TimeSeriesClusterScenario.Start, TimeSeriesClusterScenario.Start.AddTicks(1), 2);

    internal AggregateSamplesRequest Aggregate() => Scenario.Aggregate(TimeSeriesClusterScenario.Start,
        TimeSeriesClusterScenario.Start.AddTicks(2), 2);
}
