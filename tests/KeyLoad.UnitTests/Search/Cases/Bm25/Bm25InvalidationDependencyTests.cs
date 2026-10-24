namespace KeyLoad.UnitTests.Search;

internal sealed class Bm25InvalidationDependencyTests
{
    [Test]
    public Task EveryDocumentMutationPublishesOnceAndCurrentPolicyChangesDoNotUseOutboxAsAuthority()
        => Bm25InvalidationDependencyFlow.RunAsync();
}
