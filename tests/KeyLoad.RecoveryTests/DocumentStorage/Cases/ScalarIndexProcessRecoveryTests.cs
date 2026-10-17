using KeyLoad.RecoveryTests.Features.StorageRecovery;

namespace KeyLoad.RecoveryTests.Features.DocumentStorage;

internal sealed class ScalarIndexProcessRecoveryTests
{
    private const string TrialRootPrefix = "keyload-scalar-index-process-";
    [Test]
    public async Task AcDstoreIndexProcess001ReferenceModelSurvivesAcknowledgedAndInflightRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), TrialRootPrefix + Guid.NewGuid().ToString("N"));
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var admission = await StorageTrialLease.AcquireAsync(cancellationToken);
        await ScalarIndexProcessRecovery.RunAsync(root, cancellationToken);
    }
}
