using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.StorageReplication;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class DocumentSessionReadClusterTests(ClusterFixture fixture)
{
    [Test]
    public Task AcknowledgedDocumentTokenSurvivesSiloRestartAndRejectsInvalidReads()
        => DocumentSessionReadClusterFlow.RunAsync(fixture);
}
