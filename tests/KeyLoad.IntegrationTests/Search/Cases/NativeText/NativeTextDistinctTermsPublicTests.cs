namespace KeyLoad.IntegrationTests.Features.Search;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "native-text-distinct-terms-public")]
[NotInParallel]
internal sealed class NativeTextDistinctTermsPublicTests(ClusterFixture fixture)
{
    [Test]
    public Task DistinctTermCoverageFrequencyAndLengthPreserveLiteralPagesAfterMutationAndOriginalReplay()
        => NativeTextDistinctTermsPublicTrial.RunAsync(fixture, TestContext.Current!.Execution.CancellationToken);
}
