using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsHostedDatabase(RequestCqrsHostedNodeResources owner, ReplicaLifecycleFixture journal)
{
    private const long FirstRetainedEntry = 1;

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

    internal ReplicatedOperation ReadRetainedOperation(Guid commandId)
    {
        var last = journal.Log.State.LastIndex;
        if (last < FirstRetainedEntry || last > Database.Limits.MaxScanRecords)
        {
            throw new InvalidOperationException();
        }
        var entries = journal.Log.Read(FirstRetainedEntry, checked((int)last), Database.Limits.MaxBatchBytes);
        if (entries.Length != last)
        {
            throw new InvalidOperationException();
        }
        return entries.SingleOrDefault(entry => entry.Operation?.Id == commandId)?.Operation
            ?? throw new InvalidOperationException();
    }

    internal OperationResult Submit<T>(OperationKind kind, T payload, string principal,
        Guid? id, DateTimeOffset? time, CancellationToken cancellationToken)
    {
        if (time is not null)
        {
            throw new ArgumentException(RequestCqrsHostedProtocol.LeaderEvaluationTime, nameof(time));
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
