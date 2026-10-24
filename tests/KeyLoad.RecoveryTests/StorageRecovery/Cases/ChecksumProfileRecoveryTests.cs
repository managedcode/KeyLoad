namespace KeyLoad.RecoveryTests.StorageRecovery;

internal sealed class ChecksumProfileRecoveryTests
{
    [Test]
    public async Task CurrentWalPreserves257RecordsAndBothAppendsAcrossFourProcesses()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        using var admission = await StorageTrialLease.AcquireAsync(token);
        await ChecksumProfileProcess.RunAsync(token);
    }
}
