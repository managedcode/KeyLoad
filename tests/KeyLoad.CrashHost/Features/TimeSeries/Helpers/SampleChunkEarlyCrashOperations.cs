using KeyLoad.Core;

namespace KeyLoad.CrashHost;

internal static class SampleChunkEarlyCrashOperations
{
    internal static OperationResult Configure(DatabaseEngine database) => CrashDatabase.Submit(database,
        OperationKind.ConfigureResource, new ConfigureResourceRequest(SampleChunkCrashContract.Partition.TenantId,
            SampleChunkCrashContract.Partition.DatabaseId, new(SampleChunkCrashContract.Set,
                ResourceKind.TimeSeries, SampleChunkCrashContract.Partition.TransactionDomainId)),
        SampleChunkCrashContract.AcknowledgedId);

    internal static void Seed(DatabaseEngine database, SampleChunkEarlyCut cut)
    {
        Configure(database).Get<ResourceDefinition>();
        if (cut == SampleChunkEarlyCut.Open)
        { return; }
        SampleChunkCrashOperations.Commit(database, Guid.NewGuid(), Open()).Get<CommitReceipt>();
        if (cut == SampleChunkEarlyCut.Append)
        { return; }
        SampleChunkCrashOperations.Commit(database, Guid.NewGuid(), Append()).Get<CommitReceipt>();
        SampleChunkCrashOperations.Commit(database, Guid.NewGuid(), SampleChunkCrashOperations.Intended(false))
            .Get<CommitReceipt>();
    }

    internal static Mutation Intended(SampleChunkEarlyCut cut) => cut switch
    {
        SampleChunkEarlyCut.Open => Open(),
        SampleChunkEarlyCut.Append => Append(),
        SampleChunkEarlyCut.Correction => new AppendSamples(SampleChunkCrashContract.Set,
            SampleChunkCrashContract.Series, [SampleChunkCrashContract.Late], SampleChunkCrashContract.Tags),
        _ => throw new InvalidOperationException(SampleChunkCrashContract.Invalid)
    };

    internal static void Complete(DatabaseEngine database, SampleChunkEarlyCut cut)
    {
        if (cut == SampleChunkEarlyCut.Open)
        { SampleChunkCrashOperations.Commit(database, Guid.NewGuid(), Append()).Get<CommitReceipt>(); }
        if (cut != SampleChunkEarlyCut.Correction)
        {
            SampleChunkCrashOperations.Commit(database, Guid.NewGuid(), SampleChunkCrashOperations.Intended(false))
                .Get<CommitReceipt>();
            SampleChunkCrashOperations.Commit(database, Guid.NewGuid(), Intended(SampleChunkEarlyCut.Correction))
                .Get<CommitReceipt>();
        }
        SampleChunkCrashOperations.Commit(database, Guid.NewGuid(), SampleChunkCrashOperations.Intended(true))
            .Get<CommitReceipt>();
    }

    private static OpenSampleChunkWindow Open() => new(SampleChunkCrashContract.Set, SampleChunkCrashContract.Series,
        SampleChunkCrashContract.WindowId, SampleChunkCrashContract.From, SampleChunkCrashContract.Until);
    private static AppendSamples Append() => new(SampleChunkCrashContract.Set, SampleChunkCrashContract.Series,
        [SampleChunkCrashContract.First, SampleChunkCrashContract.Equal], SampleChunkCrashContract.Tags);
}
