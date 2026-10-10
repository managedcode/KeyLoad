namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal sealed class NativeChecksumProfileRecoveryTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CurrentNativeWalPreservesExactCommittedCutAcrossOppositeIntrinsicProfilesAndHealthyCold(bool firstScalar)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        using var admission = await StorageTrialLease.AcquireAsync(token);
        await NativeChecksumProfileProcess.RunAsync(firstScalar, token);
    }
}
