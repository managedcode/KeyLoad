using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.BackupRestore.Serialization;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.BackupRestore;

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
    private readonly string directory = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
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
        OpenStore();
        InitializeAuthority();
    }

    internal OperationResult Batch(PartitionRef partition, params Mutation[] mutations)
        => Batch(partition, Guid.NewGuid(), mutations);

    internal OperationResult Batch(PartitionRef partition, Guid commandId, params Mutation[] mutations)
        => Batch(partition, commandId, NoAppliedIndex, mutations);

    internal OperationResult Batch(PartitionRef partition, Guid commandId, long replicationIndex,
        params Mutation[] mutations)
        => Submit(OperationKind.Batch, new CommandRequest(commandId, partition, [.. mutations]), commandId, replicationIndex);

    internal OperationResult Submit<T>(OperationKind kind, T payload, Guid? id = null,
        long replicationIndex = NoAppliedIndex)
        => Database.Apply(new(id ?? Guid.NewGuid(), kind, RootPrincipalId,
            Database.EvaluationClock.GetUtcNow(), JsonSerializer.Serialize(payload, JsonDefaults.Options)), replicationIndex);

    internal AtomicPartitionCatalogEntryV1? ReadEntry(PartitionRef partition)
        => Store.Read(view => view.GetRecord<AtomicPartitionCatalogEntryV1>(AtomicPartitionRosterKeys.Partition(partition)));

    internal byte[]? ReadEntryBytes(PartitionRef partition)
        => Store.Read(view => view.ReadOwnedValue(AtomicPartitionRosterKeys.Partition(partition)));

    internal void ConfigureAuxiliaryCollection(string name)
        => RequireConfigured(new ResourceDefinition(name, ResourceKind.Collection, Source.TransactionDomainId));

    internal void ConfigureTenantCollection(string tenant, string name)
    {
        var result = Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(tenant, DatabaseId,
                new ResourceDefinition(name, ResourceKind.Collection, TransactionDomainId)));
        if (result.Error is not null)
        {
            throw new InvalidOperationException(result.SafeDetail ?? ConfigurationFailure);
        }
    }

    internal void Reopen()
    {
        Store.Dispose();
        OpenStore();
        Database = CreateDatabase(Store);
    }

    internal void ConfigureGraph()
    {
        var collection = new ResourceDefinition(Collection, ResourceKind.Collection, Source.TransactionDomainId);
        var graph = new ResourceDefinition(Graph, ResourceKind.Graph, Source.TransactionDomainId);
        RequireConfigured(collection);
        RequireConfigured(graph);
    }

    private void RequireConfigured(ResourceDefinition definition)
    {
        var result = Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(Source.TenantId, Source.DatabaseId, definition));
        if (result.Error is not null)
        {
            throw new InvalidOperationException(result.SafeDetail ?? ConfigurationFailure);
        }
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
        { ClusterAdministrator = true }, DatabaseEngine.Credential(RootPrincipalId, RootPrincipalId, RootCredential));
        PhysicalShardTestBootstrap.Bootstrap(Database, RootPrincipalId);
    }

    private DatabaseEngine CreateDatabase(ZoneTreeStore store)
        => new(store, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(limits), UnitExecutionOptions.DueWork(),
            UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(),
            UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(),
            UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution(), UnitExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance, timeProvider);

    public void Dispose()
    {
        try
        {
            Store.Dispose();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
