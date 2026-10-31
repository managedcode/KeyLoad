using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;

namespace KeyLoad.ProcessRecoveryTests.OrleansExecution;

internal sealed class RequestCqrsHostedDatabase(PartitionHost owner)
{
    private const string MissingCreation = "The original successful resource creation command is absent or invalid.";
    internal DatabaseEngine Database => owner.Database;
    internal string Directory => owner.DirectoryPath;
    internal ZoneTreeStore Store => (ZoneTreeStore)Database.Store;
    internal PartitionRef Partition
    {
        get;
    } = new(RequestCqrsHostedProtocol.Tenant, RequestCqrsHostedProtocol.Database,
        RequestCqrsHostedProtocol.Domain, RequestCqrsHostedProtocol.Partition);
    internal ICommitCoordinator Coordinator => owner.Coordinator;
    internal (long AppliedPosition, long StorePosition) ReadCanonicalCut()
        => Store.Read(view => (view.ReadOwnedValue(KeySpace.AppliedBytes) is { } bytes
            ? OrleansSerialization.Deserialize<long>(bytes) : default(long), Store.Position));

    internal Result<object> Submit<T>(OperationKind kind, T payload, string principal = RequestCqrsHostedProfile.Root,
        Guid? id = null, DateTimeOffset? time = null, CancellationToken cancellationToken = default) where T : notnull
    {
        if (time is not null)
        {
            throw new ArgumentException(RequestCqrsHostedProtocol.CoordinatorEvaluationTime, nameof(time));
        }
        owner.Coordinator.ReadBarrierAsync(cancellationToken).GetAwaiter().GetResult();
        var result = owner.Coordinator.RuntimeSubmitAsync(kind, id ?? Guid.NewGuid(), principal,
            payload, cancellationToken: cancellationToken).GetAwaiter().GetResult();
        owner.Coordinator.ReadBarrierAsync(cancellationToken).GetAwaiter().GetResult();
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
            new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId, resource),
            RequestCqrsHostedProfile.Root, id, null, TestContext.Current!.Execution.CancellationToken).Get<ResourceDefinition>();
    }

    internal EntityRef Reference(Guid creation, string collection, string id)
    {
        var expected = new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId,
            new(collection, ResourceKind.Collection, Partition.TransactionDomainId));
        var retained = Store.Read(view => view.GetRecord<StoredOutcome>(
            OutcomeStoreOracle.GlobalKey(RequestCqrsHostedProfile.Root, creation)))
            ?? throw new InvalidOperationException(MissingCreation);
        var original = retained.PreparedCommand ?? throw new InvalidOperationException(MissingCreation);
        var payload = original.Value ?? throw new InvalidOperationException(MissingCreation);
        var body = ((ConfigureResourceRequest)payload.Input);
        if (retained.Result.IsFailed || original.CommandId != creation || payload.Kind != OperationKind.ConfigureResource
            || original.UserId != RequestCqrsHostedProfile.Root
            || !JsonDefaults.Serialize(body).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected)))
        {
            throw new InvalidOperationException(MissingCreation);
        }
        var lifetime = ResourceLifetimeIdentity.Create(original, expected.TenantId,
            expected.DatabaseId, expected.Definition);
        return new(Partition, expected.Definition.Name, id)
        {
            Lifetime = new(lifetime.Incarnation.Value)
        };
    }

    internal CommitReceipt Commit(params Mutation[] mutations)
    {
        var id = Guid.NewGuid();
        return Submit(OperationKind.Batch, new CommandRequest(id, Partition, [.. mutations]),
            RequestCqrsHostedProfile.Root, id, null, TestContext.Current!.Execution.CancellationToken).Get<CommitReceipt>();
    }
}
