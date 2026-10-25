using System.Text.Json;
using KeyLoad.Core.Authorization;
using KeyLoad.Core.BackupRestore.Execution;
using KeyLoad.Core.BackupRestore.Serialization;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Core.StoragePartitions.Contracts;
using KeyLoad.RecoveryTests.StorageRecovery;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.BackupRestore;

internal sealed class AtomicPartitionRosterFixture : IDisposable
{
    private const string DirectoryPrefix = "keyload-partition-roster-";
    private const string GuidFormat = "N";
    private const string RootPrincipalId = "root";
    private const string RootTenantId = "system";
    private const string Wildcard = "*";
    private const string RootCredential = "root.unit-test-credential-32-characters";
    private const string ConfigurationFailure = "The real roster fixture resource configuration failed.";
    private const string TenantId = "tenant";
    private const string DatabaseId = "database";
    private const string TransactionDomainId = "orders";
    private const long NoAppliedIndex = 0;
    internal const string Collection = "roster-documents";
    internal const string Graph = "roster-graph";
    private const string SourcePartitionKey = "source";
    private const string DestinationPartitionKey = "destination";
    internal static readonly PartitionRef Source = new(TenantId, DatabaseId, TransactionDomainId, SourcePartitionKey);
    internal static readonly PartitionRef Destination = new(TenantId, DatabaseId, TransactionDomainId, DestinationPartitionKey);
    private readonly string directory = Path.Combine(StorageTrialDirectory.ResolveExistingDirectory(new(AppContext.BaseDirectory)), DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
    private readonly DatabaseLimits limits;
    private readonly int? maxFrameBytes;
    private readonly TimeProvider timeProvider = TimeProvider.System;

    internal ZoneTreeStore Store { get; private set; } = null!;
    internal DatabaseEngine Database { get; private set; } = null!;
    internal string JournalPath => Path.Combine(directory, ZoneTreePersistenceFormat.JournalFileName);

    internal AtomicPartitionRosterFixture(DatabaseLimits? configuredLimits = null, int? configuredMaxFrameBytes = null)
    {
        limits = configuredLimits ?? new DatabaseLimits();
        maxFrameBytes = configuredMaxFrameBytes;
        var acquiring = true;
        try
        {
            OpenStore();
            acquiring = false;
            InitializeAuthority();
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
        {
            TestDatabaseDisposal.Rollback(error, Store, acquiring, DeleteDirectory);
            throw;
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
        {
            TestDatabaseDisposal.Rollback(error, Store, acquiring, DeleteDirectory);
            throw;
        }
    }

    internal Result<object> Batch(PartitionRef partition, params Mutation[] mutations)
        => Batch(partition, Guid.NewGuid(), mutations);

    internal Result<object> Batch(PartitionRef partition, Guid commandId, params Mutation[] mutations)
        => Batch(partition, commandId, NoAppliedIndex, mutations);

    internal Result<object> Batch(PartitionRef partition, Guid commandId, long sequence,
        params Mutation[] mutations)
        => Submit(OperationKind.Batch, new CommandRequest(commandId, partition, [.. mutations]), commandId, sequence);

    internal Result<object> Submit<T>(OperationKind kind, T payload, Guid? id = null,
        long sequence = NoAppliedIndex)
    {
        var operation = Database.CreateCommand(kind, id ?? Guid.NewGuid(), RootPrincipalId,
            Database.EvaluationClock.GetUtcNow(), JsonSerializer.Serialize(payload, JsonDefaults.Options));
        return sequence == NoAppliedIndex
            ? Database.Apply(operation)
            : Database.ApplyStorageOperation(operation, sequence, CancellationToken.None).Result;
    }

    internal AtomicPartitionCatalogEntry? ReadEntry(PartitionRef partition)
        => Store.Read(view => view.GetRecord<AtomicPartitionCatalogEntry>(AtomicPartitionRosterKeys.Partition(partition)));

    internal byte[]? ReadEntryBytes(PartitionRef partition)
        => Store.Read(view => view.ReadOwnedValue(AtomicPartitionRosterKeys.Partition(partition)));

    internal void ConfigureTenantCollection(string tenant, string name)
    {
        var result = Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(tenant, DatabaseId,
                new ResourceDefinition(name, ResourceKind.Collection, TransactionDomainId)));
        if (Errors.Code(result.Problem) is not null)
        {
            throw new InvalidOperationException(result.Problem?.Detail ?? ConfigurationFailure);
        }
    }

    internal void Reopen()
    {
        Store.Dispose();
        OpenStore();
        Database = CreateDatabase(Store);
    }

    internal ResourceLifetime ConfigureGraph()
    {
        var collection = new ResourceDefinition(Collection, ResourceKind.Collection, Source.TransactionDomainId);
        var graph = new ResourceDefinition(Graph, ResourceKind.Graph, Source.TransactionDomainId);
        var lifetime = RequireConfigured(collection);
        RequireConfigured(graph);
        return lifetime;
    }

    private ResourceLifetime RequireConfigured(ResourceDefinition definition)
    {
        var request = new ConfigureResourceRequest(Source.TenantId, Source.DatabaseId, definition);
        var original = Database.CreateCommand(OperationKind.ConfigureResource, Guid.NewGuid(), RootPrincipalId,
            Database.EvaluationClock.GetUtcNow(), JsonSerializer.Serialize(request, JsonDefaults.Options));
        var result = Database.Apply(original);
        if (Errors.Code(result.Problem) is not null)
        {
            throw new InvalidOperationException(result.Problem?.Detail ?? ConfigurationFailure);
        }
        return ResourceLifetimeIdentity.Create(original, Source.TenantId, Source.DatabaseId, definition);
    }

    private void OpenStore()
    {
        var options = maxFrameBytes is { } frameBytes
            ? new ZoneTreeStoreOptions(directory) { MaxFrameBytes = frameBytes }
            : new ZoneTreeStoreOptions(directory);
        Store = new(options, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution(),
            timeProvider: timeProvider);
    }

    private void InitializeAuthority()
    {
        Database = CreateDatabase(Store);
        Database.Bootstrap(new(RootPrincipalId, RootTenantId, [new(Wildcard, Wildcard, Capability.All)], [Wildcard])
        {
            ClusterAdministrator = true
        }, AuthorizationExecution.Credential(RootPrincipalId, RootPrincipalId, RootCredential));
    }

    private DatabaseEngine CreateDatabase(ZoneTreeStore store)
        => new(store, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(limits), UnitExecutionOptions.DueWork(),
            UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(),
            UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.RuntimeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution(), timeProvider);

    internal void WriteLocalDocument(PartitionRef partition, string id, string json, ResourceLifetime lifetime)
    {
        var reference = new EntityRef(partition, Collection, id) { Lifetime = new(lifetime.Incarnation.Value) };
        var record = new DocumentRecord(reference, 1, json, new RowAccess(), Database.EvaluationClock.GetUtcNow());
        Store.Commit((transaction, position) =>
        {
            var roster = new AtomicPartitionRosterTransaction(transaction, position, NoAppliedIndex);
            roster.Put(KeySpace.Partition(PartitionRecordFamilies.Document, partition, Collection, id),
                OrleansSerialization.Serialize(record));
            roster.PersistCandidates();
            roster.ValidateCommit();
            return true;
        });
    }

    public void Dispose()
    {
        Store.Dispose();
        DeleteDirectory();
    }

    private void DeleteDirectory()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
