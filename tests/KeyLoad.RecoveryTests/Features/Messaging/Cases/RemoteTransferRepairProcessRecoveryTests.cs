using KeyLoad.CrashHost.Features.Messaging;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal sealed class RemoteTransferRepairProcessRecoveryTests
{
    private const string TrialPrefix = "keyload-transfer-policy-repair-crash-";
    private const int FirstMutation = 0;

    [Test]
    [Arguments(CommitStage.HeaderWritten, QueueTransferRepairStage.Accept)]
    [Arguments(CommitStage.JournalFlushed, QueueTransferRepairStage.Accept)]
    [Arguments(CommitStage.ApplyCompleted, QueueTransferRepairStage.Accept)]
    [Arguments(CommitStage.HeaderWritten, QueueTransferRepairStage.Complete)]
    [Arguments(CommitStage.JournalFlushed, QueueTransferRepairStage.Complete)]
    [Arguments(CommitStage.ApplyCompleted, QueueTransferRepairStage.Complete)]
    public Task OriginalSourceRepairCasCrashRetainsExactDeniedOutcomeAtomicHistoryAndColdHealthy(
        CommitStage stage, QueueTransferRepairStage transferStage)
        => MessagingCrashTrial.RunAsync(TrialPrefix, transferStage == QueueTransferRepairStage.Accept
            ? RemoteTransferRepairCrashProtocol.AcceptMode : RemoteTransferRepairCrashProtocol.CompleteMode, stage,
            FirstMutation, RemoteTransferRepairProcessRecovery.VerifyAsync, TestContext.Current!.Execution.CancellationToken);
}
