namespace KeyLoad.FunctionalTests.BackupRestore;

internal sealed class MixedEventingRetentionBackupTests
{
    [Test]
    public Task RetainedEventsQueueLifecycleAndTargetInboxSharePausedBackupCutThenAuthorizedColdContinuation()
        => MixedEventingRetentionBackupTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
