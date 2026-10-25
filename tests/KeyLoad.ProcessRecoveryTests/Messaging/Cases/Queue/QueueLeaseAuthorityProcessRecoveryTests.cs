using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.ProcessRecoveryTests.Messaging;

internal sealed class QueueLeaseAuthorityProcessRecoveryTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task StoppedProcessOriginalNackRefusesSelectedLeaseFaultThenColdRepairReplayAndHealthyWork(bool foreignReference)
        => MessagingCrashTrial.RunAsync("keyload-queue-lease-authority-crash-", QueueOrderedRetryCrashProtocol.Mode,
            CommitStage.HeaderWritten, 0, (root, _, token) => QueueLeaseAuthorityProcessTrial.RunAsync(root, foreignReference, token),
            TestContext.Current!.Execution.CancellationToken);
}
