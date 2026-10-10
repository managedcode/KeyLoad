namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextCapturedPublicOperationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OriginalPublicNativeReaderOverlapsCompletedUpdateDeleteSwapThenJoinsAndCompletesHealthyReplayAndCold(bool cancelOriginal)
        => await NativeTextCapturedPublicFlow.RunAsync(cancelOriginal, TestContext.Current!.Execution.CancellationToken);
    [Test]
    public async Task RevokedOriginalPublicReaderReturnsNoPrivateRowsThenPersistedRepairRequiresFreshPublicationAndColdHealthyOperation()
        => await NativeTextCapturedPolicyFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
    [Test]
    public async Task GenuineBoundedCapturedAdmissionRefusalJoinsActualOwnerBeforeFullPublicHealthyReceiptReplayAndCold()
        => await NativeTextCapturedBudgetFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
    [Test]
    public async Task GenuineSameOwnerSnapshotReplacementWhileOriginalPublicFtsReaderRemainsHeldRefusesOldCutThenFreshRebindCompletesFullReplayAndCold()
        => await NativeTextCapturedSnapshotFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
