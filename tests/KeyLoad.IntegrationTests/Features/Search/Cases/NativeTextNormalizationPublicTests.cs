namespace KeyLoad.IntegrationTests.Features.Search;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "native-text-normalization-public")]
[NotInParallel]
internal sealed class NativeTextNormalizationPublicTests(ClusterFixture fixture)
{
    [Test]
    public Task CanonicalUnicodeAndRepeatedQueryTermsPreserveLiteralPublicPagesAfterMutationAndOriginalReplay()
        => NativeTextNormalizationPublicTrial.RunAsync(fixture, TestContext.Current!.Execution.CancellationToken);
}
