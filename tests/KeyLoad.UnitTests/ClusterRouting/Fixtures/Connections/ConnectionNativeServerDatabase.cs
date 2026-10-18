using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Borrows the actual canonical database and submits setup through its native Orleans coordinator.</summary>
internal sealed class ConnectionNativeServerDatabase(Func<RequestCqrsHostedRuntimeFixture> source,
    Func<CancellationToken> originalToken)
{
    private const string Tenant = "tenant";
    private const string DatabaseName = "database";
    private const string Domain = "orders";
    private const string PartitionKey = "customer-1";
    private const string CoordinatorTime = "The native coordinator owns evaluation time.";
    private const string MissingEntry = "The original native command entry is absent or outside the bounded retained image.";
    internal DatabaseEngine Database => source().Database.Database;
    internal ZoneTreeStore Store => source().Database.Store;
    internal string Directory => source().Database.Directory;
    internal PartitionRef Partition { get; } = new(Tenant, DatabaseName, Domain, PartitionKey);
    internal ICommitCoordinator Coordinator => source().Database.Coordinator;

    internal Result<object> Submit<T>(OperationKind kind, T payload,
        string principal = ConnectionNativeProtocol.Root, Guid? id = null, DateTimeOffset? time = null)
    {
        if (time is not null)
        {
            throw new ArgumentException(CoordinatorTime, nameof(time));
        }
        var token = originalToken();
        var work = new ReadExecutionBudget(Database.OperationLimitsOptions, source().Clock, token);
        Coordinator.ReadBarrierAsync(token).GetAwaiter().GetResult();
        var result = Coordinator.SubmitNativeAsync(kind, id ?? Guid.NewGuid(), principal,
            NativeSerialization.Serialize(payload), work, token).GetAwaiter().GetResult();
        Coordinator.ReadBarrierAsync(token).GetAwaiter().GetResult();
        return result;
    }

    internal ResourceDefinition Configure(string name, ResourceKind kind, string? domain = null,
        IndexDefinition[]? indexes = null, SensitiveFieldPolicy[]? fields = null, QueuePolicy? queuePolicy = null,
        Guid? id = null)
    {
        var resource = new ResourceDefinition(name, kind, domain ?? Partition.TransactionDomainId)
        {
            Indexes = [.. indexes ?? []],
            FieldPolicies = [.. fields ?? []],
            QueuePolicy = queuePolicy ?? new()
        };
        return Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId, resource), id: id).Get<ResourceDefinition>();
    }

    internal CommitReceipt Commit(params Mutation[] mutations)
    {
        var id = Guid.NewGuid();
        return Submit(OperationKind.Batch, new CommandRequest(id, Partition, [.. mutations]), id: id)
            .Get<CommitReceipt>();
    }

    internal (long AppliedPosition, long StorePosition) ReadCanonicalCut()
        => source().Database.ReadCanonicalCut();

    internal StoredOutcome ReadStoredOutcome(Guid id)
        => OutcomeStoreOracle.ReadStored(Database, id, originalToken());

    internal EntityRef Reference(Guid creation, string collection, string id)
    {
        var expected = new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId,
            new(collection, ResourceKind.Collection, Partition.TransactionDomainId));
        var original = ReadStoredOutcome(creation).PreparedCommand
            ?? throw new InvalidOperationException(MissingEntry);
        var body = NativeSerialization.Deserialize<ConfigureResourceRequest>((original).Value!.Input.Span);
        if (original.CommandId != creation || original.Value!.Kind != OperationKind.ConfigureResource
            || original.UserId! != ConnectionNativeProtocol.Root
            || !JsonDefaults.Serialize(body).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected)))
        {
            throw new InvalidOperationException(MissingEntry);
        }
        var lifetime = ResourceLifetimeIdentity.Create(original, expected.TenantId,
            expected.DatabaseId, expected.Definition);
        return new(Partition, expected.Definition.Name, id)
        {
            Lifetime = new(lifetime.Incarnation.Value)
        };
    }
}
