namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal sealed class NativeChecksumProfileRecoveryTests
{
    [Test]
    public async Task CurrentNativeWalPreserves257RecordsAndBothAppendsAcrossFourProcesses()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        using var admission = await StorageTrialLease.AcquireAsync(token);
        await NativeChecksumProfileProcess.RunAsync(token);
    }
}
