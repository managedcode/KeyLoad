namespace KeyLoad.IntegrationTests.Features.BlobStorage;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "blob-read-policy-public")]
[NotInParallel]
internal sealed class BlobReadPolicyPublicTests(ClusterFixture fixture)
{
    [Test]
    public async Task PersistedReadGrantRemovalAndRepairControlMetadataAndEveryRangeOnOriginalPublicSessions()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await BlobReadPolicyPublicTrial.RunAsync(fixture, token);
        await BlobPrincipalRevokedR44Trial.RunAsync(fixture, token);
    }
}
