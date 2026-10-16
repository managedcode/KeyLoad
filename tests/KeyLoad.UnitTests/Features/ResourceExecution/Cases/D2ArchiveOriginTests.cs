namespace KeyLoad.UnitTests.Features.ResourceExecution;

[NotInParallel]
internal sealed class D2ArchiveOriginTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task VerifiedNativeArchiveRestorePreservesProvenOriginAndNeverPromotesUnknownWithExactReceiptsAndTwoCold(bool unknownSource)
        => await D2ArchiveTrial.ExecuteAsync(unknownSource, TestContext.Current!.Execution.CancellationToken);
}
