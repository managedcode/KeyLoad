using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class ResourcePolicyUpdateFixture : IDisposable
{
    internal const string TenantId = "policy-tenant";
    internal const string DatabaseId = "policy-database";
    internal const string DomainId = "policy-domain";
    internal const string PartitionKey = "policy-partition";
    internal const string Collection = "policy-records";
    internal const string DocumentId = "record-1";
    internal const string RootId = "root";
    internal const string ReaderId = "policy-reader";
    internal const string SystemTenant = "system";
    internal const string RootCredential = "root.resource-policy-test-credential";
    internal const string SecretField = "/secret";
    internal const string PublicDocument = "{\"public\":\"visible\",\"secret\":\"classified\"}";

    private const string DirectoryPrefix = "keyload-resource-policy-";
    private const string Wildcard = "*";
    private bool opened;

    internal string DirectoryPath { get; }
    internal PartitionRef Partition { get; } = new(TenantId, DatabaseId, DomainId, PartitionKey);
    internal ZoneTreeStore Store { get; private set; } = null!;
    internal DatabaseEngine Database { get; private set; } = null!;

    internal ResourcePolicyUpdateFixture(long initialSchemaVersion = 1)
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString("N"));
        Open();
        Database.Bootstrap(new(RootId, SystemTenant, [new(Wildcard, Wildcard, Capability.All)], [Wildcard])
        { ClusterAdministrator = true }, DatabaseEngine.Credential(RootId, RootId, RootCredential));
        PhysicalShardTestBootstrap.Bootstrap(Database, RootId);
        Apply(ConfigureResource(new(Collection, ResourceKind.Collection, DomainId)
        { SchemaVersion = initialSchemaVersion })).Get<ResourceDefinition>();
        Commit(new PutDocument(Collection, DocumentId, PublicDocument));
        ConfigureReader();
    }

    internal ResourceDefinition Resource()
        => Store.Read(view => view.GetRecord<ResourceDefinition>(ResourceKey()))
            ?? throw new InvalidOperationException("The resource policy fixture must own its resource.");

    internal byte[]? ResourceBytes() => Store.Read(view => view.ReadOwnedValue(ResourceKey()));

    internal byte[]? DocumentBytes() => Store.Read(view => view.ReadOwnedValue(
        KeyLoad.Core.Features.DocumentStorage.DocumentStorageKeys.RecordKey(Partition, Collection, DocumentId)));

    internal byte[]? OutcomeBytes(Guid operationId) => Store.Read(view => view.ReadOwnedValue(
        KeySpace.GlobalOutcome(RootId, operationId)));

    internal ReplicatedOperation ConfigureResource(ResourceDefinition definition, long? expectedSchemaVersion = null,
        Guid? operationId = null, string principalId = RootId)
    {
        var payload = new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId, definition)
        { ExpectedSchemaVersion = expectedSchemaVersion };
        return new(operationId ?? Guid.NewGuid(), OperationKind.ConfigureResource, principalId,
            TimeProvider.System.GetUtcNow(), JsonSerializer.Serialize(payload, JsonDefaults.Options));
    }

    internal OperationResult Apply(ReplicatedOperation operation) => Database.Apply(operation);

    private CommitReceipt Commit(Mutation mutation)
    {
        var commandId = Guid.NewGuid();
        var request = new CommandRequest(commandId, Partition, [mutation]);
        var operation = new ReplicatedOperation(commandId, OperationKind.Batch, RootId,
            TimeProvider.System.GetUtcNow(), JsonSerializer.Serialize(request, JsonDefaults.Options));
        return Database.Apply(operation).Get<CommitReceipt>();
    }

    internal void ConfigureReader()
    {
        var principal = new PrincipalRecord(ReaderId, TenantId,
            [new(DatabaseId, Collection, Capability.DocumentsRead)], []);
        Database.Apply(new(Guid.NewGuid(), OperationKind.ConfigurePrincipal, RootId,
            TimeProvider.System.GetUtcNow(), JsonSerializer.Serialize(new ConfigurePrincipalRequest(principal),
                JsonDefaults.Options))).Get<PrincipalRecord>();
    }

    internal void Reopen()
    {
        Store.Dispose();
        Open();
        Database.Bootstrap(new(RootId, SystemTenant, [new(Wildcard, Wildcard, Capability.All)], [Wildcard])
        { ClusterAdministrator = true }, DatabaseEngine.Credential(RootId, RootId, RootCredential));
    }

    internal static KeyLoadException Failure(OperationResult result)
    {
        var exception = Assert.ThrowsExactly<KeyLoadException>(() => result.Get<ResourceDefinition>());
        return exception!;
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

    private byte[] ResourceKey() => KeySpace.Resource(Partition.TenantId, Partition.DatabaseId, Collection);

    private void Open()
    {
        if (!opened && System.IO.Directory.Exists(DirectoryPath))
        {
            throw new InvalidOperationException("The resource policy fixture must own a fresh directory.");
        }
        Store = new(new(DirectoryPath), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        Database = new(Store, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource());
        opened = true;
    }
}
