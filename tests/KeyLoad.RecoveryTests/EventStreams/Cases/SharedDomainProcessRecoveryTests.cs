using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.EventStreams;

internal sealed class SharedDomainProcessRecoveryTests
{
    [Test]
    [Arguments(CommitStage.HeaderWritten, SharedDomainCrashProtocol.FirstMutation)]
    [Arguments(CommitStage.PayloadWritten, SharedDomainCrashProtocol.FirstMutation)]
    [Arguments(CommitStage.JournalFlushed, SharedDomainCrashProtocol.FirstMutation)]
    [Arguments(CommitStage.MutationApplied, SharedDomainCrashProtocol.FirstMutation)]
    [Arguments(CommitStage.MutationApplied, SharedDomainCrashProtocol.MiddleMutation)]
    [Arguments(CommitStage.MutationApplied, SharedDomainCrashProtocol.LaterMutation)]
    [Arguments(CommitStage.ApplyCompleted, SharedDomainCrashProtocol.FirstMutation)]
    public async Task SameLiteralAndProducerIdProcessCutsPreserveFourAcknowledgedScopesAndOneWholeOriginalScope(CommitStage stage, int mutation)
        => await SharedDomainProcessTrial.RunAsync(stage, mutation, TestContext.Current!.Execution.CancellationToken);
}
