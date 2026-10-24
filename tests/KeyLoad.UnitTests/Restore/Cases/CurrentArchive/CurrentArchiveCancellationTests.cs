namespace KeyLoad.UnitTests.BackupRestore;

internal sealed class CurrentArchiveCancellationTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public Task CurrentArchiveCancellationPreservesWholeCutThenSamePathRestoresAndTwoCold(
        bool checkpoint, bool existingEmpty)
        => CurrentArchiveCancellationFlow.RunAsync(checkpoint, existingEmpty,
            TestContext.Current!.Execution.CancellationToken);
}
