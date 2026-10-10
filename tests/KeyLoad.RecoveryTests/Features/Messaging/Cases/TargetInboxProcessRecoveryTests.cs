using KeyLoad.CrashHost.Features.Messaging;
using KeyLoad.Storage.ZoneTree;
namespace KeyLoad.RecoveryTests.Features.Messaging;

internal sealed class TargetInboxProcessRecoveryTests
{
    private const string TrialPrefix = "keyload-target-inbox-crash-";
    [Test]
    [Arguments(CommitStage.HeaderWritten, 0)]
    [Arguments(CommitStage.PayloadWritten, 0)]
    [Arguments(CommitStage.JournalFlushed, 0)]
    [Arguments(CommitStage.MutationApplied, 0)]
    [Arguments(CommitStage.MutationApplied, 1)]
    [Arguments(CommitStage.MutationApplied, 2)]
    [Arguments(CommitStage.ApplyCompleted, 0)]
    public Task TargetInboxCrashRecoversWholeTargetEffectsReceiptAndQuotaWhileSourceAckIsSeparateThenColdHealthy(CommitStage stage, int index)
        => MessagingCrashTrial.RunAsync(TrialPrefix, TargetInboxCrashProtocol.Mode, stage, index,
            TargetInboxProcessRecovery.VerifyAsync, TestContext.Current!.Execution.CancellationToken);
}
