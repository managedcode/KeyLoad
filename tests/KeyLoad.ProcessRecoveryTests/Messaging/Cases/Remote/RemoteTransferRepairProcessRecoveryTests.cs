using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.ProcessRecoveryTests.Messaging;

internal sealed class RemoteTransferRepairProcessRecoveryTests
{
    private const string TrialPrefix = "keyload-transfer-policy-repair-crash-";
    private const int FirstMutation = 0;

    [Test]
    [Arguments(CommitStage.WriteAheadLogSynced, QueueTransferRepairStage.Accept)]
    [Arguments(CommitStage.ApplyCompleted, QueueTransferRepairStage.Accept)]
    [Arguments(CommitStage.WriteAheadLogSynced, QueueTransferRepairStage.Complete)]
    [Arguments(CommitStage.ApplyCompleted, QueueTransferRepairStage.Complete)]
    public Task OriginalSourceRepairCasCrashRetainsExactDeniedOutcomeAtomicHistoryAndColdHealthy(
        CommitStage stage, QueueTransferRepairStage transferStage)
        => MessagingCrashTrial.RunAsync(TrialPrefix, transferStage == QueueTransferRepairStage.Accept
            ? RemoteTransferRepairCrashProtocol.AcceptMode : RemoteTransferRepairCrashProtocol.CompleteMode, stage,
            FirstMutation, RemoteTransferRepairProcessRecovery.VerifyAsync, TestContext.Current!.Execution.CancellationToken);
}
