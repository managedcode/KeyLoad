using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests;

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

    private const string NativeOperationRequired = "This operation requires the fixture-owned native ordered apply path.";
    private const string NativeObservationRequired = "This observation requires the fixture-owned native ordered apply path.";
    private const string CatalogOwnerRequired = "The owned fixture requires its actual native catalog owner.";
    private const string UnexpectedReplicaDirectory = "A native fixture replica directory requires native admission.";
    private const string OriginalOwnerRequired = "The original native owner is required.";

    private readonly TestDatabaseReplicaAdmission? replicaAdmission;
    private readonly TestDatabaseCloseState closeState = new();
    private readonly Func<ZoneTreeStore, PhysicalShardRecord?, DatabaseEngine> createDatabase;
    private readonly IOptions<DatabaseLimits> limits;
    public string Directory
    {
        get;
    }
    public ZoneTreeStore Store
    {
        get;
    }
    internal IOptions<ZoneTreeStorageExecutionOptions> StorageExecution { get; } = UnitExecutionOptions.StorageExecution();
    public DatabaseEngine Database
    {
        get;
    }
    internal IOptions<PartitionMovementCheckpointOptions> MovementCheckpoints { get; } = UnitExecutionOptions.MovementCheckpoints();
    public PartitionRef Partition { get; } = new(TenantId, DatabaseId, TransactionDomainId, PartitionKey);
    public TestDatabase(DatabaseLimits? limits = null, string? directory = null,
        bool bootstrapPhysicalShardCatalog = true, BlobExecutionOptions? blobExecution = null,
        NativeClaimsExecutionOptions? claimsExecution = null, TimeProvider? timeProvider = null,
        bool nativeReplicaAdmission = false, TimeSeriesExecutionOptions? timeSeriesExecution = null,
        string? nativeReplicaDirectory = null)
    {
        Directory = directory ?? Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
        if (System.IO.Directory.Exists(Directory))
        {
            throw new ArgumentException(FreshDirectoryRequired, nameof(directory));
        }
        if (!nativeReplicaAdmission && nativeReplicaDirectory is not null)
        {
            throw new ArgumentException(UnexpectedReplicaDirectory, nameof(nativeReplicaDirectory));
        }
        ZoneTreeStore? acquired = null;
        var nativeAcquisitionStarted = false;
        var blobOptions = UnitExecutionOptions.BlobExecution(blobExecution);
        var claimsOptions = UnitExecutionOptions.NativeClaimsExecution(claimsExecution);
        try
        {
            var clock = timeProvider ?? TimeProvider.System;
            Store = acquired = new(new(Directory), StorageExecution, UnitExecutionOptions.PointCacheExecution(), timeProvider: clock);
            this.limits = UnitExecutionOptions.DatabaseLimits(limits);
            createDatabase = TestDatabaseEngineFactory.Capture(this.limits, blobOptions, claimsOptions,
                timeSeriesExecution, MovementCheckpoints, clock);
            Database = createDatabase(Store, null);
            TestDatabaseEngineFactory.Bootstrap(Database, RootPrincipalId, RootCredential, bootstrapPhysicalShardCatalog);
            if (nativeReplicaAdmission)
            {
                Database = CreateConfiguredDatabase(Store);
                nativeAcquisitionStarted = true;
                replicaAdmission = new(this, nativeReplicaDirectory);
            }
        }
        catch (Exception primary)
        {
            TestDatabaseNativeDisposal.Rollback(primary, acquired, nativeAcquisitionStarted, DeleteOwnedDirectory);
            throw;
        }
    }
    public OperationResult Submit<T>(OperationKind kind, T payload, string principal = RootPrincipalId, Guid? id = null, DateTimeOffset? time = null)
    {
        var operation = new ReplicatedOperation(id ?? Guid.NewGuid(), kind, principal,
            time ?? default, JsonSerializer.Serialize(payload, JsonDefaults.Options));
        if (replicaAdmission is { } native)
        {
            return native.Submit(operation, time.HasValue);
        }
        return time.HasValue ? Database.Apply(operation) : Database.ApplyEmbedded(operation, cancellationToken: default);
    }

    internal OperationResult SubmitIssued(ReplicatedOperation operation)
        => replicaAdmission is { } native ? native.Submit(operation, explicitTime: true)
            : throw new InvalidOperationException(NativeOperationRequired);

    internal void JoinNativeReadBarrier(CancellationToken cancellationToken)
    {
        if (replicaAdmission is not { } native)
        {
            throw new InvalidOperationException(NativeOperationRequired);
        }
        native.ReadBarrier(cancellationToken);
    }

    internal OperationResult SubmitIssuedEmbedded(ReplicatedOperation operation, CancellationToken cancellationToken)
        => replicaAdmission is { } native ? native.Submit(operation, explicitTime: false, cancellationToken)
            : throw new InvalidOperationException(NativeOperationRequired);

    internal DatabaseEngine CreateConfiguredDatabase(ZoneTreeStore ownedStore)
    {
        var owner = ownedStore.Read(view => PhysicalShardCatalogRecordSerialization.Read(view)?.DefaultShard)
            ?? throw new InvalidOperationException(CatalogOwnerRequired);
        return createDatabase(ownedStore, owner);
    }

    internal ReadExecutionBudget CreateReadWork(CancellationToken cancellationToken)
        => new(limits, Database.EvaluationClock, cancellationToken);

    internal void ReconcileCatalogRestore(IAtomicTransaction transaction, StoreIdentity identity,
        long position, ReadOnlyMemory<byte> metadata, ImmutableArray<ClusterBackupOwnerCut> cuts,
        ImmutableArray<ClusterRestoreOwnerMapping> mappings, ReadExecutionBudget work,
        string credential = RootCredential)
        => ClusterRestoreCatalogReconciliation.Reconcile(transaction, identity, position, metadata,
            credential, cuts, mappings, limits, Database.EvaluationClock, work);

    internal TestDatabaseReplicaAdmission CreateTargetAdmission(DatabaseEngine targetDatabase, ZoneTreeStore target,
        string directory, string replicaDirectory, Guid? originalNodeId) => new(targetDatabase, target, directory,
            replicaAdmission?.ExecutionOptions ?? throw new InvalidOperationException(OriginalOwnerRequired), replicaDirectory, originalNodeId);

    internal void JoinOwnedResources()
    {
        lock (closeState.Gate)
        {
            if (closeState.TryBeginJoin())
            {
                JoinCore();
            }
        }
    }

    internal (long LastIndex, long CommittedIndex, long Term) ReadJournalCut()
        => replicaAdmission is { } native ? native.ReadJournalCut()
            : throw new InvalidOperationException(NativeObservationRequired);

    internal KeyLoad.Replication.ReplicaEntry ReadRetainedEntry(Guid originalId)
        => replicaAdmission is { } native ? native.ReadRetainedEntry(originalId)
            : throw new InvalidOperationException(NativeObservationRequired);

    internal KeyLoad.Replication.ReplicaEntry ReadRetainedEntry(KeyLoad.ReplicatedOperation original)
        => replicaAdmission is { } native ? native.ReadRetainedEntry(original)
            : throw new InvalidOperationException(NativeObservationRequired);

    internal KeyLoad.Replication.ReplicaEntry RecoverFaultOwnedRow(ReplicatedOperation original,
        Action restoreFaultOwnedRow, List<Exception> failures, CancellationToken cancellationToken,
        ErrorCode? expectedApplyError = null)
        => replicaAdmission is { } native
            ? native.RecoverFaultOwnedRow(original, restoreFaultOwnedRow, failures, cancellationToken, expectedApplyError)
            : throw new InvalidOperationException(NativeOperationRequired);

    public ResourceDefinition Configure(string name, ResourceKind kind, string? domain = null, IndexDefinition[]? indexes = null,
        SensitiveFieldPolicy[]? fields = null, QueuePolicy? queuePolicy = null)
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

    private void JoinCore()
    {
        var failures = new List<Exception>();
        try
        {
            replicaAdmission?.Dispose();
        }
        catch (Exception failure) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(failure) is not null)
        {
            failures.Add(failure);
        }
        try
        {
            Store.Dispose();
        }
        catch (Exception failure) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(failure) is not null)
        {
            failures.Add(failure);
        }
        closeState.CompleteJoin(failures);
    }

    private void DeleteOwnedDirectory()
    {
        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, true);
        }
    }
}
