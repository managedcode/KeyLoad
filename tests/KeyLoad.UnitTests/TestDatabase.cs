using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests;

public sealed class TestDatabase : IDisposable
{
    public string Directory { get; } = Path.Combine(Path.GetTempPath(), "keyload-unit-" + Guid.NewGuid().ToString("N"));
    public ZoneTreeStore Store { get; }
    public DatabaseEngine Database { get; }
    public PartitionRef Partition { get; } = new("tenant", "database", "orders", "customer-1");
    public TestDatabase()
    {
        Store = new(new(Directory));
        Database = new(Store, new AuthorizationPolicy());
        Database.Bootstrap(new("root", "system", [new("*", "*", Capability.All)], ["*"]) { ClusterAdministrator = true },
            DatabaseEngine.Credential("root", "root", "root.unit-test-credential-32-characters"));
    }
    public OperationResult Submit<T>(OperationKind kind, T payload, string principal = "root", Guid? id = null, DateTimeOffset? time = null)
        => Database.Apply(new(id ?? Guid.NewGuid(), kind, principal, time ?? DateTimeOffset.UtcNow, JsonSerializer.Serialize(payload, JsonDefaults.Options)));
    public ResourceDefinition Configure(string name, ResourceKind kind, string? domain = null, IndexDefinition[]? indexes = null,
        SensitiveFieldPolicy[]? fields = null, QueuePolicy? queuePolicy = null)
    {
        var resource = new ResourceDefinition(name, kind, domain ?? Partition.TransactionDomainId)
        { Indexes = indexes ?? [], FieldPolicies = fields ?? [], QueuePolicy = queuePolicy ?? new() };
        return Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId, resource)).Get<ResourceDefinition>();
    }
    public CommitReceipt Commit(params Mutation[] mutations)
    {
        var id = Guid.NewGuid();
        return Submit(OperationKind.Batch, new CommandRequest(id, Partition, mutations), id: id).Get<CommitReceipt>();
    }
    public void Dispose() { Store.Dispose(); System.IO.Directory.Delete(Directory, true); }
}
