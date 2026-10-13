using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class EventSchemaCursorPublicTests(ClusterFixture fixture)
{
    [Test]
    public async Task PersistedHeaderPolicySchemaFencesOldSignedCursorsAndRepairPreservesOriginalReceiptsAcrossTwoColdOwners()
    {
        using var deadline = McpCallerDeadline.Create();
        await EventSchemaCursorPublicFlow.RunAsync(fixture, deadline.Token);
    }
}
