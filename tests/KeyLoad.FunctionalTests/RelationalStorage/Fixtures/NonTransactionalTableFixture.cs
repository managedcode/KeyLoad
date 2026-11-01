using KeyLoad.Core.DocumentStorage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;

namespace KeyLoad.FunctionalTests.RelationalStorage;

/// <summary>Owns real asynchronous table commands, their source store and native backup destinations.</summary>
internal sealed class NonTransactionalTableFixture(Action<CommitStage, long, int>? storeFaultObserver = null) : IDisposable
{
    internal const string AtomicTable = "atomic-rows";
    internal const string Beta = "beta";
    internal const string Alpha = "alpha";
    internal const string Third = "third";
    internal const string Later = "later";
    internal const string PlainDirectory = "plain-tree";
    internal const string BackupDirectory = "backup";
    internal const string RestoredDirectory = "restored";
    internal const string CopyDirectory = "copy";
    internal TestDatabase Owner { get; } = new(storeFaultObserver: storeFaultObserver);
    internal DatabaseEngine Database => Owner.Database;
    internal PartitionRef Partition => Owner.Partition;
    internal ZoneTreeStore Store => Owner.Store;

    internal Task<Result<object>> ConfigureAsync(bool transactional = false, string name = RelationalTestData.Table)
        => SubmitAsync(OperationKind.ConfigureResource, new ConfigureResourceRequest(Partition.TenantId,
            Partition.DatabaseId, RelationalTestData.Definition() with { Name = name, Transactional = transactional }));

    internal Task<Result<object>> SubmitAsync(OperationKind kind, object input, Guid? commandId = null)
        => new EmbeddedCoordinator(Database).RuntimeSubmitAsync(kind, commandId ?? Guid.NewGuid(),
            RelationalTestData.Root, input, cancellationToken: TestContext.Current!.Execution.CancellationToken);

    internal Task<Result<object>> BatchAsync(Guid id, params Mutation[] mutations)
        => SubmitAsync(OperationKind.Batch, new CommandRequest(id, Partition, [.. mutations]), id);

    internal byte[] RowKey(string id = RelationalTestData.First)
        => DocumentStorageKeys.RecordKey(Partition, RelationalTestData.Table, id);

    internal byte[] OutcomeKey(Guid id) => KeySpace.PartitionOutcome(Partition, RelationalTestData.Root, id);

    internal string Destination(string name) => Path.Combine(Owner.Directory, name);

    internal static ZoneTreeStore Open(string directory)
        => new(new(directory), UnitExecutionOptions.StorageExecution());

    internal static string Row(string id, string name = Alpha)
        => RelationalTestData.Row(id).Replace(Alpha, name, StringComparison.Ordinal);

    public void Dispose() => Owner.Dispose();
}
