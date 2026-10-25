using KeyLoad.ProcessRecoveryTests.StorageRecovery;

namespace KeyLoad.ProcessRecoveryTests.DocumentStorage;

internal sealed class CompositeIndexProcessRecoveryTests
{
    private const string TrialRootPrefix = "keyload-composite-index-process-";
    [Test]
    public async Task AcDstoreIndexProcess001ReferenceModelSurvivesAcknowledgedAndInflightRestart()
    {
        var root = Path.Combine(AppContext.BaseDirectory, TrialRootPrefix + Guid.NewGuid().ToString("N"));
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var admission = await StorageTrialLease.AcquireAsync(cancellationToken);
        await CompositeIndexProcessRecovery.RunAsync(root, cancellationToken);
    }
}
