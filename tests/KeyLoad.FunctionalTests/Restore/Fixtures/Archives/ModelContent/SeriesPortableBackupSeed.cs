using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.BackupRestore;

internal static class SeriesPortableBackupSeed
{
    internal const string Root = "root";
    internal const string Set = "backup_series";
    internal const string Series = "cpu";
    private const string EventPrefix = "portable-sample-";
    internal static readonly DateTimeOffset Start = new(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
    internal static DateTimeOffset Floor => Start.AddSeconds(2);

    internal static SampleData Data(int id, int seconds, double value)
        => new(EventPrefix + id, Start.AddSeconds(seconds), value);

    internal static Command<DatabaseCommand> Batch(DatabaseEngine database, PartitionRef partition, params Mutation[] mutations)
    {
        var id = Guid.NewGuid();
        return database.CreateCommand(OperationKind.Batch, id, Root, database.EvaluationClock.GetUtcNow(),
            new CommandRequest(id, partition, [.. mutations]));
    }

    internal static async Task<(Command<DatabaseCommand> Operation, Result<object> Result)[]> RunAsync(
        BackupIndexedDocumentDedupFixture fixture, CancellationToken token)
    {
        var database = fixture.Source.Database;
        var resource = new ConfigureResourceRequest(fixture.Partition.TenantId, fixture.Partition.DatabaseId,
            new ResourceDefinition(Set, ResourceKind.TimeSeries, fixture.Partition.TransactionDomainId));
        var configured = database.CreateCommand(OperationKind.ConfigureResource, Guid.NewGuid(), Root,
            database.EvaluationClock.GetUtcNow(), resource);
        await Assert.That(Errors.Code(database.ApplyEmbedded(configured, token).Problem).Value).IsNull();
        var appended = Batch(database, fixture.Partition,
            new AppendSamples(Set, Series, [Data(1, 0, 1), Data(2, 1, 2), Data(3, 2, 3), Data(4, 3, 4)]));
        var appendResult = database.ApplyEmbedded(appended, token);
        await SeriesPortableBackupAssertions.ReceiptAsync(database, fixture.Partition, appended, appendResult);
        var expired = Batch(database, fixture.Partition, new ExpireSamples(Set, Series, Floor, 1));
        var expireResult = database.ApplyEmbedded(expired, token);
        await SeriesPortableBackupAssertions.ReceiptAsync(database, fixture.Partition, expired, expireResult);
        await SeriesPortableBackupAssertions.VisibleAsync(database, fixture.Partition, false, token);
        return [(appended, appendResult), (expired, expireResult)];
    }
}
