using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests;

/// <summary>Owns a real canonical ZoneTree store for local Core operation qualification.</summary>
internal sealed class TestDatabase : IDisposable
{
    private const string DirectoryPrefix = "keyload-unit-";
    private const string GuidFormat = "N";
    private const string FreshDirectoryRequired = "The fixture must own a fresh directory.";
    private const string TenantId = "tenant";
    private const string DatabaseId = "database";
    private const string TransactionDomainId = "orders";
    private const string PartitionKey = "customer-1";
    private const string RootPrincipalId = "root";
    private const string RootCredential = "root.unit-test-credential-32-characters";
    private readonly TestDatabaseCloseState closeState = new();
    private readonly Func<ZoneTreeStore, DatabaseEngine> createDatabase;
    private readonly IOptions<DatabaseLimits> limits;

    public string Directory { get; }
    public ZoneTreeStore Store { get; }
    internal IOptions<ZoneTreeStorageExecutionOptions> StorageExecution { get; } = UnitExecutionOptions.StorageExecution();
    public DatabaseEngine Database { get; }
    public PartitionRef Partition { get; } = new(TenantId, DatabaseId, TransactionDomainId, PartitionKey);

    public TestDatabase(DatabaseLimits? limits = null, string? directory = null,
        BlobExecutionOptions? blobExecution = null, NativeClaimsExecutionOptions? claimsExecution = null,
        TimeProvider? timeProvider = null, TimeSeriesExecutionOptions? timeSeriesExecution = null)
    {
        Directory = directory ?? Path.Combine(StorageTrialDirectory.ResolveExistingDirectory(new(Path.GetTempPath())),
            DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
        if (System.IO.Directory.Exists(Directory))
        {
            throw new ArgumentException(FreshDirectoryRequired, nameof(directory));
        }
        ZoneTreeStore? acquired = null;
        try
        {
            var clock = timeProvider ?? TimeProvider.System;
            Store = acquired = new(new(Directory), StorageExecution, UnitExecutionOptions.PointCacheExecution(), timeProvider: clock);
            this.limits = UnitExecutionOptions.DatabaseLimits(limits);
            createDatabase = TestDatabaseEngineFactory.Capture(this.limits, UnitExecutionOptions.BlobExecution(blobExecution),
                UnitExecutionOptions.NativeClaimsExecution(claimsExecution), timeSeriesExecution, clock);
            Database = createDatabase(Store);
            TestDatabaseEngineFactory.Bootstrap(Database, RootPrincipalId, RootCredential);
        }
        catch (Exception primary)
        {
            TestDatabaseNativeDisposal.Rollback(primary, acquired, nativeAcquisitionStarted: false, DeleteOwnedDirectory);
            throw;
        }
    }

    public Result<object> Submit<T>(OperationKind kind, T payload, string principal = RootPrincipalId,
        Guid? id = null, DateTimeOffset? time = null)
    {
        var operation = Database.CreateCommand(kind, id ?? Guid.NewGuid(), principal,
            time ?? default, JsonSerializer.Serialize(payload, JsonDefaults.Options));
        return time.HasValue ? Database.Apply(operation) : Database.ApplyEmbedded(operation, cancellationToken: default);
    }

    internal Result<object> SubmitIssued(Command<DatabaseCommand> operation)
        => Database.ApplyEmbedded(Database.OwnCommand(operation), CancellationToken.None);

    internal Result<object> SubmitIssuedEmbedded(Command<DatabaseCommand> operation, CancellationToken cancellationToken)
    {
        var verified = Database.OwnCommand(operation);
        return Database.ApplyEmbedded(verified, cancellationToken);
    }

    internal void JoinStorageReadCut(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Store.Read(_ =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Store.Position;
        });
    }

    internal StoredOutcome ReadStoredOutcome(Guid commandId)
        => OutcomeStoreOracle.ReadStored(Database, commandId, CancellationToken.None);

    internal StoredOutcome ReadStoredOutcome(Command<DatabaseCommand> command)
        => OutcomeStoreOracle.ReadStored(Store, command)
            ?? throw new InvalidOperationException("The exact stored test outcome is missing.");

    internal DatabaseEngine CreateConfiguredDatabase(ZoneTreeStore ownedStore) => createDatabase(ownedStore);
    internal (long AppliedPosition, long StorePosition) ReadCanonicalCut()
        => Store.Read(view => (view.ReadOwnedValue(KeySpace.AppliedBytes) is { } bytes
            ? NativeSerialization.Deserialize<long>(bytes) : default(long), Store.Position));
    internal ReadExecutionBudget CreateReadWork(CancellationToken cancellationToken)
        => new(limits, Database.EvaluationClock, cancellationToken);

    internal void JoinOwnedResources()
    {
        lock (closeState.Gate)
        {
            if (closeState.TryBeginJoin())
            {
                var failures = new List<Exception>();
                KeyLoad.Server.ServerFailureObserver.Observe(Store.Dispose, failures);
                closeState.CompleteJoin(failures);
            }
        }
    }

    public ResourceDefinition Configure(string name, ResourceKind kind, string? domain = null,
        IndexDefinition[]? indexes = null, SensitiveFieldPolicy[]? fields = null, QueuePolicy? queuePolicy = null)
        => TestDatabaseResourceConfiguration.Apply(this, name, kind, domain, indexes, fields, queuePolicy);

    public CommitReceipt Commit(params Mutation[] mutations)
    {
        var id = Guid.NewGuid();
        return Submit(OperationKind.Batch, new CommandRequest(id, Partition, [.. mutations]), id: id).Get<CommitReceipt>();
    }

    public void Dispose()
    {
        JoinOwnedResources();
        closeState.Delete(DeleteOwnedDirectory);
    }

    private void DeleteOwnedDirectory()
    {
        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, true);
        }
    }
}
