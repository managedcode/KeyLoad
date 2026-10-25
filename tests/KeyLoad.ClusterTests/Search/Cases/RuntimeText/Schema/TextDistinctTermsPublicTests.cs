namespace KeyLoad.ClusterTests.Search;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "execution-text-distinct-terms-public")]
[NotInParallel]
internal sealed class TextDistinctTermsPublicTests(ClusterFixture fixture)
{
    [Test]
    public Task DistinctTermCoverageFrequencyAndLengthPreserveLiteralPagesAfterMutationAndOriginalReplay()
        => TextDistinctTermsPublicTrial.RunAsync(fixture, TestContext.Current!.Execution.CancellationToken);
}
