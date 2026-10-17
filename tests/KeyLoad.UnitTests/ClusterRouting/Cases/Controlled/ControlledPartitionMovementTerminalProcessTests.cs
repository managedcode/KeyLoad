using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class ControlledPartitionMovementTerminalProcessTests
{
    [Test]
    [Arguments(CommitStage.HeaderWritten)]
    [Arguments(CommitStage.PayloadWritten)]
    [Arguments(CommitStage.JournalFlushed)]
    [Arguments(CommitStage.MutationApplied)]
    [Arguments(CommitStage.ApplyCompleted)]
    public Task GenuineInstallFourChildCutsContinueThroughPublicationRetirementAndFreshColdWrite(CommitStage cut)
        => ControlledPartitionMovementTerminalProcessTrial.ExecuteAsync(PartitionMovePeerStage.Install, cut);

    [Test]
    [Arguments(CommitStage.HeaderWritten)]
    [Arguments(CommitStage.PayloadWritten)]
    [Arguments(CommitStage.JournalFlushed)]
    [Arguments(CommitStage.MutationApplied)]
    [Arguments(CommitStage.ApplyCompleted)]
    public Task GenuineRetireFourChildCutsPreserveCompleteOriginalReceiptAndFreshColdWrite(CommitStage cut)
        => ControlledPartitionMovementTerminalProcessTrial.ExecuteAsync(PartitionMovePeerStage.Retire, cut);
    [Test]
    [Arguments(CommitStage.HeaderWritten)]
    [Arguments(CommitStage.PayloadWritten)]
    [Arguments(CommitStage.JournalFlushed)]
    [Arguments(CommitStage.MutationApplied)]
    [Arguments(CommitStage.ApplyCompleted)]
    public Task GenuineJoinedSourceAbortFourChildCutsPreserveModelsOriginalReceiptAndHealthyColdWrite(CommitStage cut)
        => ControlledPartitionMovementTerminalProcessTrial.ExecuteAsync(PartitionMovePeerStage.Abort, cut);
}
