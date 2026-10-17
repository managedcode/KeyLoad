using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class DocumentSessionReadRf3Tests(ClusterFixture fixture)
{
    [Test]
    public Task Kl021AcknowledgedDocumentTokenSurvivesElectedFailoverAndRejectsInvalidReads()
        => DocumentSessionReadRf3Flow.RunAsync(fixture);
}
