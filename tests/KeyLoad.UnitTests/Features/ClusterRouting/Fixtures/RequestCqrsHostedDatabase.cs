using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ClusterReplication;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsHostedDatabase(RequestCqrsHostedNodeResources owner, ReplicaLifecycleFixture journal)
{
    internal DatabaseEngine Database => journal.Database;
    internal string Directory => journal.CanonicalDirectoryPath;
    internal ZoneTreeStore Store => (ZoneTreeStore)Database.Store;
    internal PartitionRef Partition
    {
        get;
    } = new(RequestCqrsHostedProtocol.Tenant, RequestCqrsHostedProtocol.Database,
        RequestCqrsHostedProtocol.Domain, RequestCqrsHostedProtocol.Partition);
    internal ICommitCoordinator Coordinator => owner.Coordinator;
    internal (long LastIndex, long CommittedIndex, long Term) ReadJournalCut()
    {
        var original = journal.Log.State;
        return (original.LastIndex, original.CommittedIndex, original.Term);
    }


    internal OperationResult Submit<T>(OperationKind kind, T payload, string principal = RequestCqrsHostedProfile.Root,
        Guid? id = null, DateTimeOffset? time = null)
    {
        if (time is not null)
        {
            throw new ArgumentException(RequestCqrsHostedProtocol.LeaderEvaluationTime, nameof(time));
        }
        return owner.Coordinator.SubmitNativeAsync(kind, id ?? Guid.NewGuid(), principal,
            NativeSerialization.Serialize(payload)).GetAwaiter().GetResult();
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
            new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId, resource)).Get<ResourceDefinition>();
    }

    internal CommitReceipt Commit(params Mutation[] mutations)
    {
        var id = Guid.NewGuid();
        return Submit(OperationKind.Batch, new CommandRequest(id, Partition, [.. mutations]), id: id).Get<CommitReceipt>();
    }
}
