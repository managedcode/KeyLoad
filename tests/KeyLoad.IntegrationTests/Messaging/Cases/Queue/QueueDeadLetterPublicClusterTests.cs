using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey), NotInParallel]
internal sealed class QueueDeadLetterPublicClusterTests(ClusterFixture fixture)
{
    [Test, Arguments(false), Arguments(true)]
    public Task TerminalNackRetainsCompleteDlqThroughQuotaAndPersistedDenialThenHealthySiblingAndSameOwnerCold(bool mcpFirst)
        => QueueDeadLetterClusterTrial.RunAsync(fixture, mcpFirst);
}
