namespace KeyLoad.IntegrationTests.Features.BlobStorage;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "blob-complete-cas-public")]
[NotInParallel]
internal sealed class BlobCompleteCasPublicTests(ClusterFixture fixture)
{
    [Test]
    public Task TwoStagedUploadsAtSameRevisionHaveOnePublicationAndOriginalConflictSurvivesRepairAndReplay()
        => BlobCompleteCasPublicTrial.RunAsync(fixture, TestContext.Current!.Execution.CancellationToken);
}
