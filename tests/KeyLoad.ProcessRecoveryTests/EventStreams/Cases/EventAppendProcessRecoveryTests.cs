using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.ProcessRecoveryTests.EventStreams;

internal sealed class EventAppendProcessRecoveryTests
{
    private const int IntermediateMutation = 3;
    private const int LaterMutation = 6;

    [Test]
    [Arguments(CommitStage.HeaderWritten, 0)]
    [Arguments(CommitStage.PayloadWritten, 0)]
    [Arguments(CommitStage.JournalFlushed, 0)]
    [Arguments(CommitStage.MutationApplied, 0)]
    [Arguments(CommitStage.MutationApplied, IntermediateMutation)]
    [Arguments(CommitStage.MutationApplied, LaterMutation)]
    [Arguments(CommitStage.ApplyCompleted, 0)]
    public async Task AcEventCrash001002SeededProducerRecoversOneWholeCutAndStableReplay(CommitStage stage, int index)
        => await EventAppendCrashTrial.RunAsync(stage, index, TestContext.Current!.Execution.CancellationToken);
}
