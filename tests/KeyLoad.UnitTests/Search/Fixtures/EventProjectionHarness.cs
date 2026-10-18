using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class EventProjectionHarness : IDisposable
{
    private const string DirectoryPrefix = "keyload-projection-";
    private const string RootId = "root";
    private const string SystemTenant = "system";
    private const string Wildcard = "*";
    private const string RootCredential = "root.projection-test-credential-32-characters";
    private const string PartitionKey = "customer-1";
    private readonly bool bootstrapped;

    internal string DirectoryPath
    {
        get;
    }
    internal ZoneTreeStore Store { get; private set; } = null!;
    internal DatabaseEngine Database { get; private set; } = null!;
    internal PartitionRef Partition { get; } = new("tenant", "database", "orders", PartitionKey);

    internal EventProjectionHarness()
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString("N"));
        Open();
        Database.Bootstrap(new(RootId, SystemTenant, [new(Wildcard, Wildcard, Capability.All)], [Wildcard])
        {
            ClusterAdministrator = true
        }, DatabaseEngine.Credential(RootId, RootId, RootCredential));
        PhysicalShardTestBootstrap.Bootstrap(Database, RootId);
        bootstrapped = true;
    }

    internal Result<object> Submit<T>(OperationKind kind, T payload, string principal = RootId, Guid? id = null)
    {
        var requestId = id ?? Guid.NewGuid();
        var operation = Database.CreateCommand(kind, requestId, principal, TimeProvider.System.GetUtcNow(), JsonSerializer.Serialize(payload, JsonDefaults.Options));
        return Database.Apply(operation);
    }

    internal ResourceDefinition Configure(string name, ResourceKind kind, SensitiveFieldPolicy[]? fields = null)
    {
        var resource = new ResourceDefinition(name, kind, Partition.TransactionDomainId) { FieldPolicies = [.. fields ?? []] };
        return Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId, resource)).Get<ResourceDefinition>();
    }

    internal CommitReceipt Commit(params Mutation[] mutations)
    {
        var id = Guid.NewGuid();
        return Submit(OperationKind.Batch, new CommandRequest(id, Partition, [.. mutations]), id: id)
            .Get<CommitReceipt>();
    }

    internal void Reopen()
    {
        Store.Dispose();
        Open();
        PhysicalShardTestBootstrap.RequireExisting(Database);
    }

    public void Dispose()
    {
        try
        {
            Store.Dispose();
        }
        finally
        {
            if (System.IO.Directory.Exists(DirectoryPath))
            {
                System.IO.Directory.Delete(DirectoryPath, true);
            }
        }
    }

    private void Open()
    {
        if (System.IO.Directory.Exists(DirectoryPath) && !bootstrapped)
        {
            throw new InvalidOperationException("The projection fixture must own a fresh directory.");
        }
        Store = new(new(DirectoryPath), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        Database = new(Store, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution());
    }
}
