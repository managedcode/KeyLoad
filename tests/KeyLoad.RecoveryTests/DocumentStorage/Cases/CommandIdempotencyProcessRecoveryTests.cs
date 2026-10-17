using KeyLoad.RecoveryTests.Features.StorageRecovery;

namespace KeyLoad.RecoveryTests.Features.DocumentStorage;

internal sealed class CommandIdempotencyProcessRecoveryTests
{
    internal const int RunTimeoutSeconds = 90;
    internal const int CleanupTimeoutSeconds = 30;

    [Test]
    public async Task AcDocument006OneHundredCommandRetriesSurviveRealProcessRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-document-command-restart-" + Guid.NewGuid().ToString("N"));
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var admission = await StorageTrialLease.AcquireAsync(cancellationToken);
        await CommandIdempotencyProcess.RunAsync(root, cancellationToken);
    }
}
