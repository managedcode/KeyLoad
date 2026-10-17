namespace KeyLoad.UnitTests.Features.BackupRestore;

/// <summary>Supporting native owner admission controls; genuine successful cluster capture remains RF3.</summary>
internal sealed class ClusterBackupAdmissionWholeFlowTests
{
    /// <summary>Accepted direct native producers saturate the quota, then settle and ordinary backup continues.</summary>
    [Test]
    public async Task ActualNativeCaptureOverloadRejectsWithoutEffectsThenSettledFailuresPermitHealthyBackup()
        => await ClusterBackupAdmissionWholeFlow.RequireAsync(false, TestContext.Current!.Execution.CancellationToken);

    /// <summary>Shutdown closes admission and joins every actual accepted producer before native storage closes.</summary>
    [Test]
    public async Task ActualNativeShutdownJoinsEveryAcceptedCaptureAndRejectsNewDirectAdmission()
        => await ClusterBackupAdmissionWholeFlow.RequireAsync(true, TestContext.Current!.Execution.CancellationToken);
}
