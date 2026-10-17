using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey), NotInParallel]
internal sealed class QueueDeadLetterPublicRf3Tests(ClusterFixture fixture)
{
    [Test, Arguments(false), Arguments(true)]
    public Task TerminalNackRetainsCompleteDlqThroughQuotaAndPersistedDenialThenHealthySiblingAndSameOwnerCold(bool mcpFirst)
        => QueueDeadLetterRf3Trial.RunAsync(fixture, mcpFirst);
}
