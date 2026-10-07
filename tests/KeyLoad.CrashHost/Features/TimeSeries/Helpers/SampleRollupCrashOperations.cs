using KeyLoad.Core;

namespace KeyLoad.CrashHost;

internal static class SampleRollupCrashOperations
{
    private const long FirstRevision = 1;
    private const long RecoveredRevision = 2;
    private const long RefreshedRevision = 3;
    private const long DroppedRevision = 4;
    private const int HalfMinute = 30;
    private const string FirstId = "a";
    private const string SecondId = "b";
    private const string ThirdId = "c";
    private const string BoundaryId = "boundary";
    private const int FirstValue = 2;
    private const int SecondValue = 4;
    private const int ThirdValue = 6;
    private const int BoundaryValue = 8;

    internal static void Seed(DatabaseEngine database)
    {
        _ = CrashDatabase.Submit(database, OperationKind.ConfigureResource,
            new ConfigureResourceRequest(SampleRollupCrashContract.Partition.TenantId,
                SampleRollupCrashContract.Partition.DatabaseId, new(SampleRollupCrashContract.Set,
                    ResourceKind.TimeSeries, SampleRollupCrashContract.Partition.TransactionDomainId)), Guid.NewGuid()).Get<ResourceDefinition>();
        Commit(database, Guid.NewGuid(), new AppendSamples(SampleRollupCrashContract.Set, SampleRollupCrashContract.Series,
            [new(FirstId, SampleRollupCrashContract.Start, FirstValue),
                new(SecondId, SampleRollupCrashContract.Start.AddSeconds(HalfMinute), SecondValue),
                new(ThirdId, SampleRollupCrashContract.ThirdTimestamp, ThirdValue),
                new(BoundaryId, SampleRollupCrashContract.End, BoundaryValue)], SampleRollupCrashContract.Tags)).Get<CommitReceipt>();
        Commit(database, Guid.NewGuid(), new ExpireSamples(SampleRollupCrashContract.Set,
            SampleRollupCrashContract.Series, SampleRollupCrashContract.Start, SampleRollupCrashContract.SampleLimit)).Get<CommitReceipt>();
    }

    internal static OperationResult Commit(DatabaseEngine database, Guid id, Mutation mutation)
    {
        var request = new CommandRequest(id, SampleRollupCrashContract.Partition, [mutation]);
        return database.ApplyEmbedded(CrashDatabase.Operation(OperationKind.Batch, request, id), cancellationToken: default);
    }

    internal static async Task HealthyAsync(string directory, DatabaseEngine database)
    {
        Commit(database, Guid.NewGuid(), SampleRollupCrashContract.Refresh(RecoveredRevision)).Get<CommitReceipt>();
        await SampleRollupCrashState.WriteAsync(directory, SampleRollupCrashContract.HealthyRefreshFile, database);
        Commit(database, Guid.NewGuid(), SampleRollupCrashContract.Drop(RefreshedRevision)).Get<CommitReceipt>();
        await SampleRollupCrashState.WriteAsync(directory, SampleRollupCrashContract.HealthyDropFile, database);
        Commit(database, Guid.NewGuid(), SampleRollupCrashContract.Refresh(DroppedRevision)).Get<CommitReceipt>();
    }

    internal static Mutation Inflight(bool drop) => drop
        ? SampleRollupCrashContract.Drop(FirstRevision) : SampleRollupCrashContract.Refresh(FirstRevision);
}
