using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.TimeSeries;

internal sealed class ScopedSampleIdentityPublicState(TimeSeriesClusterScenario scenario,
    McpPersistedIdentity reader)
{
    internal TimeSeriesClusterScenario Scenario { get; } = scenario;
    internal McpPersistedIdentity Reader { get; } = reader;
    internal List<(CommandRequest Command, CommitReceipt Receipt)> Originals { get; } = [];
    internal OutboxStatus Outbox { get; set; } = null!;
    internal CommandRequest ValueConflict { get; } = ScopedSampleIdentityPublicExpected.ValueConflict(scenario);
    internal CommandRequest TagConflict { get; } = ScopedSampleIdentityPublicExpected.TagConflict(scenario);
}
