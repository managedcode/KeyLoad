namespace KeyLoad.UnitTests.Features.ResourceExecution;

[NotInParallel]
internal sealed class D2ResourceInventoryImportTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeImageImportPreservesConcurrentUnrelatedEnrollmentAndNeverPromotesUnknown(bool unknownTarget)
        => await D2ResourceInventoryImportTrial.ExecuteAsync(unknownTarget, TestContext.Current!.Execution.CancellationToken);
}
