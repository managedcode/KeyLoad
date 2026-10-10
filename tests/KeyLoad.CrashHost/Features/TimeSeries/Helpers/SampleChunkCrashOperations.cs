using KeyLoad.Core;

namespace KeyLoad.CrashHost;

internal static class SampleChunkCrashOperations
{
    internal static void Seed(DatabaseEngine database, bool merge)
    {
        CrashDatabase.Submit(database, OperationKind.ConfigureResource,
            new ConfigureResourceRequest(SampleChunkCrashContract.Partition.TenantId,
                SampleChunkCrashContract.Partition.DatabaseId, new(SampleChunkCrashContract.Set,
                    ResourceKind.TimeSeries, SampleChunkCrashContract.Partition.TransactionDomainId)), Guid.NewGuid())
            .Get<ResourceDefinition>();
        Commit(database, Guid.NewGuid(), new OpenSampleChunkWindow(SampleChunkCrashContract.Set,
            SampleChunkCrashContract.Series, SampleChunkCrashContract.WindowId,
            SampleChunkCrashContract.From, SampleChunkCrashContract.Until)).Get<CommitReceipt>();
        Commit(database, SampleChunkCrashContract.AcknowledgedId, new AppendSamples(SampleChunkCrashContract.Set,
            SampleChunkCrashContract.Series, [SampleChunkCrashContract.First, SampleChunkCrashContract.Equal],
            SampleChunkCrashContract.Tags)).Get<CommitReceipt>();
        if (!merge)
        { return; }
        Commit(database, Guid.NewGuid(), new SealSampleChunkWindow(SampleChunkCrashContract.Set,
            SampleChunkCrashContract.Series, SampleChunkCrashContract.WindowId,
            SampleChunkCrashContract.AppendedRevision)).Get<CommitReceipt>();
        Commit(database, Guid.NewGuid(), new AppendSamples(SampleChunkCrashContract.Set,
            SampleChunkCrashContract.Series, [SampleChunkCrashContract.Late], SampleChunkCrashContract.Tags))
            .Get<CommitReceipt>();
    }

    internal static Mutation Intended(bool merge) => merge
        ? new MergeSampleChunkWindow(SampleChunkCrashContract.Set, SampleChunkCrashContract.Series,
            SampleChunkCrashContract.WindowId, SampleChunkCrashContract.CorrectedRevision)
        : new SealSampleChunkWindow(SampleChunkCrashContract.Set, SampleChunkCrashContract.Series,
            SampleChunkCrashContract.WindowId, SampleChunkCrashContract.AppendedRevision);

    internal static OperationResult Commit(DatabaseEngine database, Guid id, Mutation mutation)
        => database.ApplyEmbedded(CrashDatabase.Operation(OperationKind.Batch,
            new CommandRequest(id, SampleChunkCrashContract.Partition, [mutation]), id), cancellationToken: default);

    internal static CommitReceipt OriginalAcknowledged(DatabaseEngine database)
        => Commit(database, SampleChunkCrashContract.AcknowledgedId, new AppendSamples(SampleChunkCrashContract.Set,
            SampleChunkCrashContract.Series, [SampleChunkCrashContract.First, SampleChunkCrashContract.Equal],
            SampleChunkCrashContract.Tags)).Get<CommitReceipt>();
}
