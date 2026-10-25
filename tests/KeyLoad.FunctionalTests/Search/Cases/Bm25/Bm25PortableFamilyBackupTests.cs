namespace KeyLoad.FunctionalTests.Search;

internal sealed class Bm25PortableFamilyBackupTests
{
    [Test]
    public Task ActualPublishedSubjectHeadsRemainExactThroughRestoreAndBothReopens()
        => Bm25PortableFamilyBackupTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
