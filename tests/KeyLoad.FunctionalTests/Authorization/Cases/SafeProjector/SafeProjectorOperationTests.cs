namespace KeyLoad.FunctionalTests.Authorization;

internal sealed class SafeProjectorOperationTests
{
    [Test]
    public async Task PersistedSourceProjectionRoundtripAndCurrentGrantHistoryRemainExactAcrossTwoColdOwners()
    {
        await SafeProjectorOperationDatabase.RunAsync(SafeProjectorOperationFlow.RunAsync,
            TestContext.Current!.Execution.CancellationToken);
    }
}
