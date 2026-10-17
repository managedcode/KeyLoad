namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

/// <summary>Genuine partial imports, target Abort and both-owner cold recovery; no process-kill or RF3 claim.</summary>
[NotInParallel]
internal sealed class D2ImportAbortRecoveryTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task TargetAbortRemovesOnlyItsCompletedImportFenceAndPreservesUnrelatedWritesAndOriginAcrossTwoColdOwners(
        bool unknownTargetOrigin)
        => D2ImportAbortTrial.RunAsync(unknownTargetOrigin, TestContext.Current!.Execution.CancellationToken);
}
