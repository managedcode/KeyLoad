namespace KeyLoad.FunctionalTests.Search;

internal sealed class TextCapturedPublicOperationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OriginalPublicReaderOverlapsCompletedUpdateDeleteSwapThenJoinsAndCompletesHealthyReplayAndCold(bool cancelOriginal)
        => await TextCapturedPublicFlow.RunAsync(cancelOriginal, TestContext.Current!.Execution.CancellationToken);
    [Test]
    public async Task RevokedOriginalPublicReaderReturnsNoPrivateRowsThenPersistedRepairRequiresFreshPublicationAndColdHealthyOperation()
        => await TextCapturedPolicyFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
    [Test]
    public async Task GenuineSameOwnerSnapshotReplacementWhileOriginalPublicFtsReaderRemainsHeldRefusesOldCutThenFreshRebindCompletesFullReplayAndCold()
        => await TextCapturedSnapshotFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
