namespace KeyLoad.IntegrationTests.Features.BackupRestore;

[NotInParallel]
internal sealed class ClusterRestoreRf3ResumeTests
{
    [Test]
    [Arguments(NativeClusterRestoreStage.PlanPublished)]
    [Arguments(NativeClusterRestoreStage.SourcePrefixPersisted)]
    [Arguments(NativeClusterRestoreStage.Reconciled)]
    [Arguments(NativeClusterRestoreStage.IdentityPublished)]
    [Arguments(NativeClusterRestoreStage.AuthorityReset)]
    [Arguments(NativeClusterRestoreStage.ReadyPublished)]
    [Arguments(NativeClusterRestoreStage.NodesPublished)]
    public Task ActualOwnedProcessCutResumesOriginalPlanAndNativeSlotsThenWholeRf3CallersContinueCold(
        NativeClusterRestoreStage originalStage)
        => ClusterRestoreRf3Tests.RunAsync((source, seed, root, ct) =>
            ClusterRestoreRf3ResumeTrial.RunAsync(source, seed, root, originalStage, ct));
}
