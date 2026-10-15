namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class CapturedReadSelectedCancellationTests
{
    [Test]
    public Task AcCut004FrozenTraversalObservesSelectedCancellationAndPreservesBothCuts()
        => CapturedReadSelectedCancellationFlow.RunAsync();
}
