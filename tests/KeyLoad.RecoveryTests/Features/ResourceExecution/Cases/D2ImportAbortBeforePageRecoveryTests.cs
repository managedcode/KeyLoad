namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

/// <summary>Genuine missing-stage target Abort before image transfer; local native journals, not RF3 or process-kill proof.</summary>
[NotInParallel]
internal sealed class D2ImportAbortBeforePageRecoveryTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task TargetAbortBeforeAnyImagePagePreservesOtherImportOriginalTerminalReplayAndTwoColdOwners(bool unknownTargetOrigin)
        => D2ImportAbortTrial.RunBeforeFirstPageAsync(unknownTargetOrigin, TestContext.Current!.Execution.CancellationToken);
}
