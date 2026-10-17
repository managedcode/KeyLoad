using KeyLoad.Core;
namespace KeyLoad.CrashHost;

internal static class ChunkMultiblockCrashOperations
{
    internal static void Seed(DatabaseEngine database, bool merge)
    {
        SampleChunkCrashOperations.Seed(database, merge);
        Additional(database, merge).Get<CommitReceipt>();
    }

    internal static OperationResult Additional(DatabaseEngine database, bool merge)
        => SampleChunkCrashOperations.Commit(database, ChunkMultiblockCrashData.AdditionalId,
            new AppendSamples(SampleChunkCrashContract.Set, SampleChunkCrashContract.Series,
                [.. ChunkMultiblockCrashData.AdditionalFor(merge)], SampleChunkCrashContract.Tags));

    internal static Mutation Intended(bool merge) => merge
        ? new MergeSampleChunkWindow(SampleChunkCrashContract.Set, SampleChunkCrashContract.Series,
            SampleChunkCrashContract.WindowId, ChunkMultiblockCrashProtocol.PreparedMergeRevision)
        : new AppendSamples(SampleChunkCrashContract.Set, SampleChunkCrashContract.Series,
            [.. ChunkMultiblockCrashData.AutomaticSealTail], SampleChunkCrashContract.Tags);

    internal static OperationResult Fresh(DatabaseEngine database)
        => SampleChunkCrashOperations.Commit(database, ChunkMultiblockCrashData.FreshCommandId,
            new AppendSamples(SampleChunkCrashContract.Set, SampleChunkCrashContract.Series,
                [ChunkMultiblockCrashData.Fresh], SampleChunkCrashContract.Tags));

    internal static OperationResult HealthyMerge(DatabaseEngine database, bool merge)
        => SampleChunkCrashOperations.Commit(database, ChunkMultiblockCrashData.HealthyMergeId,
            new MergeSampleChunkWindow(SampleChunkCrashContract.Set, SampleChunkCrashContract.Series,
                SampleChunkCrashContract.WindowId, merge ? ChunkMultiblockCrashProtocol.HealthyMergeRevision - SampleChunkCrashContract.PositionStep
                    : ChunkMultiblockCrashProtocol.HealthySealRevision - SampleChunkCrashContract.PositionStep));
}
