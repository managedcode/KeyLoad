namespace KeyLoad.UnitTests.BackupRestore;

internal sealed class MixedEventingRetentionBackupTests
{
    [Test]
    public Task RetainedEventsQueueLifecycleAndTargetInboxSharePausedBackupCutThenAuthorizedColdContinuation()
        => MixedEventingRetentionBackupTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
