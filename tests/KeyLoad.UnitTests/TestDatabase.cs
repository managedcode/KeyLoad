using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ClusterRouting;
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
    private const string SystemTenantId = "system";
    private const string Wildcard = "*";
    private const string RootCredential = "root.unit-test-credential-32-characters";

    private readonly TestDatabaseReplicaAdmission? replicaAdmission;
    public string Directory { get; }
    public ZoneTreeStore Store { get; }
    public DatabaseEngine Database { get; }
    internal IOptions<PartitionMovementCheckpointOptions> MovementCheckpoints { get; } = UnitExecutionOptions.MovementCheckpoints();
    public PartitionRef Partition { get; } = new(TenantId, DatabaseId, TransactionDomainId, PartitionKey);
    public TestDatabase(DatabaseLimits? limits = null, string? directory = null,
        bool bootstrapPhysicalShardCatalog = true, BlobExecutionOptions? blobExecution = null,
        NativeClaimsExecutionOptions? claimsExecution = null, TimeProvider? timeProvider = null,
        bool nativeReplicaAdmission = false, TimeSeriesExecutionOptions? timeSeriesExecution = null)
    {
        Directory = directory ?? Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
        if (System.IO.Directory.Exists(Directory))
        {
            throw new ArgumentException(FreshDirectoryRequired, nameof(directory));
        }
        ZoneTreeStore? acquired = null;
        var blobOptions = UnitExecutionOptions.BlobExecution(blobExecution);
        var claimsOptions = UnitExecutionOptions.NativeClaimsExecution(claimsExecution);
        try
        {
            var clock = timeProvider ?? TimeProvider.System;
            Store = acquired = new(new(Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution(), timeProvider: clock);
            Database = new(Store, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(limits), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), blobOptions, claimsOptions, UnitExecutionOptions.TimeSeriesExecution(timeSeriesExecution), MovementCheckpoints, UnavailablePartitionMovementCheckpointVerifier.Instance, clock);
            Database.Bootstrap(new(RootPrincipalId, SystemTenantId, [new(Wildcard, Wildcard, Capability.All)], [Wildcard]) { ClusterAdministrator = true },
                DatabaseEngine.Credential(RootPrincipalId, RootPrincipalId, RootCredential));
            if (bootstrapPhysicalShardCatalog)
            {
                PhysicalShardTestBootstrap.Bootstrap(Database, RootPrincipalId);
            }
            if (nativeReplicaAdmission)
            { replicaAdmission = new(this); }
        }
        catch (Exception)
        {
            try
            {
                DisposeAcquiredStore(acquired);
            }
            finally
            {
                DeleteOwnedDirectory();
            }
            throw;
        }
    }
    public OperationResult Submit<T>(OperationKind kind, T payload, string principal = RootPrincipalId, Guid? id = null, DateTimeOffset? time = null)
    {
        var operation = new ReplicatedOperation(id ?? Guid.NewGuid(), kind, principal,
            time ?? default, JsonSerializer.Serialize(payload, JsonDefaults.Options));
        if (replicaAdmission is { } native)
        { return native.Submit(operation, time.HasValue); }
        return time.HasValue ? Database.Apply(operation) : Database.ApplyEmbedded(operation, cancellationToken: default);
    }

    internal OperationResult SubmitIssued(ReplicatedOperation operation)
        => replicaAdmission is { } native ? native.Submit(operation, explicitTime: true)
            : throw new InvalidOperationException("This operation requires the fixture-owned native ordered apply path.");

    private static void DisposeAcquiredStore(ZoneTreeStore? store) => store?.Dispose();
    public ResourceDefinition Configure(string name, ResourceKind kind, string? domain = null, IndexDefinition[]? indexes = null,
        SensitiveFieldPolicy[]? fields = null, QueuePolicy? queuePolicy = null)
    {
        var resource = new ResourceDefinition(name, kind, domain ?? Partition.TransactionDomainId)
        { Indexes = [.. indexes ?? []], FieldPolicies = [.. fields ?? []], QueuePolicy = queuePolicy ?? new() };
        return Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId, resource)).Get<ResourceDefinition>();
    }
    public CommitReceipt Commit(params Mutation[] mutations)
    {
        var id = Guid.NewGuid();
        return Submit(OperationKind.Batch, new CommandRequest(id, Partition, [.. mutations]), id: id).Get<CommitReceipt>();
    }
    public void Dispose()
    {
        if (replicaAdmission is not null)
        {
            DisposeNative();
            return;
        }
        try
        {
            Store.Dispose();
        }
        finally
        {
            DeleteOwnedDirectory();
        }
    }

    private void DisposeNative()
    {
        var failures = new List<Exception>();
        try
        { replicaAdmission?.Dispose(); }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null) { failures.Add(error); }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null) { failures.Add(error); }
        KeyLoad.Server.ServerFailureObserver.Observe(Store.Dispose, failures);
        KeyLoad.Server.ServerFailureObserver.Observe(DeleteOwnedDirectory, failures);
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private void DeleteOwnedDirectory()
    {
        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, true);
        }
    }
}
