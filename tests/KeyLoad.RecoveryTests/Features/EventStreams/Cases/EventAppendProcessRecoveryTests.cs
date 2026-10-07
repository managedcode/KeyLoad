using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.EventStreams;

internal sealed class EventAppendProcessRecoveryTests
{
    private const int InitialMutation = 0;
    private const int IntermediateMutation = 3;
    private const int LaterMutation = 6;

    [Test]
    [Arguments(CommitStage.HeaderWritten, InitialMutation)]
    [Arguments(CommitStage.PayloadWritten, InitialMutation)]
    [Arguments(CommitStage.JournalFlushed, InitialMutation)]
    [Arguments(CommitStage.MutationApplied, InitialMutation)]
    [Arguments(CommitStage.MutationApplied, IntermediateMutation)]
    [Arguments(CommitStage.MutationApplied, LaterMutation)]
    [Arguments(CommitStage.ApplyCompleted, InitialMutation)]
    public async Task AcEventCrash001002SeededProducerRecoversOneWholeCutAndStableReplay(CommitStage stage, int index)
        => await EventAppendCrashTrial.RunAsync(stage, index, TestContext.Current!.Execution.CancellationToken);
}
