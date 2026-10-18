using KeyLoad.RecoveryHost;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal sealed class QueueEnqueueProcessRecoveryTests
{
    [Test]
    [Arguments(CommitStage.HeaderWritten, 0)]
    [Arguments(CommitStage.PayloadWritten, 0)]
    [Arguments(CommitStage.JournalFlushed, 0)]
    [Arguments(CommitStage.MutationApplied, 0)]
    [Arguments(CommitStage.MutationApplied, 3)]
    [Arguments(CommitStage.MutationApplied, 6)]
    [Arguments(CommitStage.ApplyCompleted, 0)]
    public Task AcMsg001005ReadyScheduledProducerRecoversWholeAtomicCutThenOriginalReplayAndColdHealthyWork(CommitStage stage, int mutationIndex)
        => MessagingCrashTrial.RunAsync(QueueEnqueueCrashProtocol.DirectoryPrefix, QueueEnqueueCrashProtocol.Mode, stage, mutationIndex,
            QueueEnqueueProcessRecovery.VerifyAsync, TestContext.Current!.Execution.CancellationToken);
}
