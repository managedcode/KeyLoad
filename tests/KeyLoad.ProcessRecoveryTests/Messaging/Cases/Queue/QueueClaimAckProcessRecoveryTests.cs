using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.ProcessRecoveryTests.Messaging;

internal sealed class QueueClaimAckProcessRecoveryTests
{
    [Test]
    [Arguments(false, CommitStage.WriteAheadLogSynced)]
    [Arguments(false, CommitStage.MutationApplied)]
    [Arguments(false, CommitStage.ApplyCompleted)]
    [Arguments(true, CommitStage.WriteAheadLogSynced)]
    [Arguments(true, CommitStage.MutationApplied)]
    [Arguments(true, CommitStage.ApplyCompleted)]
    public Task ClaimAndAckProcessKillRecoversOneCompleteCutThenOriginalOutcomeReplayAndHealthyColdWork(bool ack, CommitStage stage)
        => MessagingCrashTrial.RunAsync("keyload-claim-ack-crash-",
            ack ? QueueClaimAckCrashProtocol.AckMode : QueueClaimAckCrashProtocol.ClaimMode, stage, 0,
            (root, cut, token) => QueueClaimAckRecovery.VerifyAsync(root, cut, ack, token),
            TestContext.Current!.Execution.CancellationToken);
}
