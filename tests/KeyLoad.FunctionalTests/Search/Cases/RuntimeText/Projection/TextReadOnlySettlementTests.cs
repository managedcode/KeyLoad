namespace KeyLoad.FunctionalTests.Search;

internal sealed class TextReadOnlySettlementTests
{
    [Test]
    public async Task PublishedReadOnlyClosePreservesManifestAndRefusesTamperedRetryBeforeColdSearch()
    {
        using var database = new TestDatabase();
        await TextReadOnlySettlementFlow.RunAsync(database, TestContext.Current!.Execution.CancellationToken);
    }
}
