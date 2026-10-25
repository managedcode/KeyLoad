using KeyLoad.Core;
using ManagedCode.Communication;
namespace KeyLoad.ProcessRecoveryTests;

internal static class ChunkMultiblockCrashOperations
{
    internal static void Seed(string root, DatabaseEngine database, bool merge)
    {
        SampleChunkCrashOperations.Seed(database, merge);
        SampleChunkSnapshotFile.CaptureExpectedToken(root, ChunkMultiblockCrashData.AdditionalId, database, SampleChunkCrashContract.Partition);
        Additional(database, merge).Get<CommitReceipt>();
    }

    internal static Result<object> Additional(DatabaseEngine database, bool merge)
        => SampleChunkCrashOperations.Commit(database, ChunkMultiblockCrashData.AdditionalId,
            new AppendSamples(SampleChunkCrashContract.Set, SampleChunkCrashContract.Series,
                [.. ChunkMultiblockCrashData.AdditionalFor(merge)], SampleChunkCrashContract.Tags));

    internal static Mutation Intended(bool merge) => merge
        ? new MergeSampleChunkWindow(SampleChunkCrashContract.Set, SampleChunkCrashContract.Series,
            SampleChunkCrashContract.WindowId, ChunkMultiblockCrashProtocol.PreparedMergeRevision)
        : new AppendSamples(SampleChunkCrashContract.Set, SampleChunkCrashContract.Series,
            [.. ChunkMultiblockCrashData.AutomaticSealTail], SampleChunkCrashContract.Tags);

    internal static Result<object> Fresh(DatabaseEngine database)
        => SampleChunkCrashOperations.Commit(database, ChunkMultiblockCrashData.FreshCommandId,
            new AppendSamples(SampleChunkCrashContract.Set, SampleChunkCrashContract.Series,
                [ChunkMultiblockCrashData.Fresh], SampleChunkCrashContract.Tags));

    internal static Result<object> HealthyMerge(DatabaseEngine database, bool merge)
        => SampleChunkCrashOperations.Commit(database, ChunkMultiblockCrashData.HealthyMergeId,
            new MergeSampleChunkWindow(SampleChunkCrashContract.Set, SampleChunkCrashContract.Series,
                SampleChunkCrashContract.WindowId, merge ? ChunkMultiblockCrashProtocol.HealthyMergeRevision - SampleChunkCrashContract.PositionStep
                    : ChunkMultiblockCrashProtocol.HealthySealRevision - SampleChunkCrashContract.PositionStep));
}
