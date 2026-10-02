using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

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

    public string Directory { get; }
    public ZoneTreeStore Store { get; }
    public DatabaseEngine Database { get; }
    public PartitionRef Partition { get; } = new(TenantId, DatabaseId, TransactionDomainId, PartitionKey);
    public TestDatabase(DatabaseLimits? limits = null, string? directory = null)
    {
        Directory = directory ?? Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
        if (System.IO.Directory.Exists(Directory))
        {
            throw new ArgumentException(FreshDirectoryRequired, nameof(directory));
        }
        ZoneTreeStore? acquired = null;
        try
        {
            Store = acquired = new(new(Directory));
            Database = new(Store, new AuthorizationPolicy(), limits);
            Database.Bootstrap(new(RootPrincipalId, SystemTenantId, [new(Wildcard, Wildcard, Capability.All)], [Wildcard]) { ClusterAdministrator = true },
                DatabaseEngine.Credential(RootPrincipalId, RootPrincipalId, RootCredential));
        }
        catch (Exception)
        {
            try
            {
                acquired?.Dispose();
            }
            finally
            {
                DeleteOwnedDirectory();
            }
            throw;
        }
    }
    public OperationResult Submit<T>(OperationKind kind, T payload, string principal = RootPrincipalId, Guid? id = null, DateTimeOffset? time = null)
        => Database.Apply(new(id ?? Guid.NewGuid(), kind, principal, time ?? TimeProvider.System.GetUtcNow(), JsonSerializer.Serialize(payload, JsonDefaults.Options)));
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
        try
        {
            Store.Dispose();
        }
        finally
        {
            DeleteOwnedDirectory();
        }
    }

    private void DeleteOwnedDirectory()
    {
        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, true);
        }
    }
}
