using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsHostedDatabase(PartitionHost owner)
{
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
        => Store.Read(_ => (Database.LastApplied, Store.Position));

    internal OperationResult Submit<T>(OperationKind kind, T payload, string principal,
        Guid? id, DateTimeOffset? time, CancellationToken cancellationToken)
    {
        if (time is not null)
        {
            throw new ArgumentException(RequestCqrsHostedProtocol.CoordinatorEvaluationTime, nameof(time));
        }
        owner.Coordinator.ReadBarrierAsync(cancellationToken).GetAwaiter().GetResult();
        var result = owner.Coordinator.SubmitNativeAsync(kind, id ?? Guid.NewGuid(), principal,
            NativeSerialization.Serialize(payload), cancellationToken: cancellationToken).GetAwaiter().GetResult();
        owner.Coordinator.ReadBarrierAsync(cancellationToken).GetAwaiter().GetResult();
        return result;
    }

    internal ResourceDefinition Configure(string name, ResourceKind kind, string? domain = null,
        IndexDefinition[]? indexes = null, SensitiveFieldPolicy[]? fields = null, QueuePolicy? queuePolicy = null)
    {
        var resource = new ResourceDefinition(name, kind, domain ?? Partition.TransactionDomainId)
        {
            Indexes = [.. indexes ?? []],
            FieldPolicies = [.. fields ?? []],
            QueuePolicy = queuePolicy ?? new()
        };
        return Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId, resource),
            RequestCqrsHostedProfile.Root, null, null, TestContext.Current!.Execution.CancellationToken).Get<ResourceDefinition>();
    }

    internal CommitReceipt Commit(params Mutation[] mutations)
    {
        var id = Guid.NewGuid();
        return Submit(OperationKind.Batch, new CommandRequest(id, Partition, [.. mutations]),
            RequestCqrsHostedProfile.Root, id, null, TestContext.Current!.Execution.CancellationToken).Get<CommitReceipt>();
    }
}
