namespace KeyLoad.IntegrationTests.Search;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "execution-text-normalization-public")]
[NotInParallel]
internal sealed class TextNormalizationPublicTests(ClusterFixture fixture)
{
    [Test]
    public Task CanonicalUnicodeAndRepeatedQueryTermsPreserveLiteralPublicPagesAfterMutationAndOriginalReplay()
        => TextNormalizationPublicTrial.RunAsync(fixture, TestContext.Current!.Execution.CancellationToken);
}
