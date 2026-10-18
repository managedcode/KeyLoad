namespace KeyLoad.UnitTests.Features.Search;

internal sealed class Bm25PortableFamilyBackupTests
{
    [Test]
    public Task ActualPublishedSubjectHeadsRemainExactThroughNativeRestoreAndBothNativeReopens()
        => Bm25PortableFamilyBackupTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
