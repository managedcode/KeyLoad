using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class DocumentSessionReadClusterTests(ClusterFixture fixture)
{
    [Test]
    public Task AcknowledgedDocumentTokenSurvivesElectedFailoverAndRejectsInvalidReads()
        => DocumentSessionReadClusterFlow.RunAsync(fixture);
}
