using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Replication;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Borrows the actual source journal and canonical database; all setup writes use its CLUSTER coordinator.</summary>
internal sealed class ConnectionNativeServerDatabase(Func<ControlledPartitionMovementNativeHost> source,
    Func<CancellationToken> originalToken)
{
    private const string Tenant = "tenant";
    private const string DatabaseName = "database";
    private const string Domain = "orders";
    private const string PartitionKey = "customer-1";
    private const string LeaderTime = "The native coordinator owns evaluation time.";
    private const string MissingEntry = "The original native command entry is absent or outside the bounded retained image.";
    private const long FirstEntry = 1;
    internal DatabaseEngine Database => source().Database;
    internal ZoneTreeStore Store => (ZoneTreeStore)source().Store;
    internal string Directory => source().Partition.DirectoryPath;
    internal PartitionRef Partition { get; } = new(Tenant, DatabaseName, Domain, PartitionKey);
    internal ICommitCoordinator Coordinator => source().Partition.Coordinator;

    internal OperationResult Submit<T>(OperationKind kind, T payload,
        string principal = ConnectionNativeProtocol.Root, Guid? id = null, DateTimeOffset? time = null)
    {
        if (time is not null)
        {
            throw new ArgumentException(LeaderTime, nameof(time));
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

    internal (long LastIndex, long CommittedIndex, long Term) ReadJournalCut()
    {
        var actual = source().Log.State;
        return (actual.LastIndex, actual.CommittedIndex, actual.Term);
    }

    internal ReplicaEntry ReadRetainedEntry(Guid id)
    {
        var log = source().Log;
        var limits = Database.Limits;
        var token = originalToken();
        log.ProtocolGate.Wait(token);
        try
        {
            var last = log.State.LastIndex;
            if (last < FirstEntry || last > limits.MaxScanRecords)
            {
                throw new InvalidOperationException(MissingEntry);
            }
            var entries = new List<ReplicaEntry>();
            for (var index = FirstEntry; index <= last; index++)
            {
                token.ThrowIfCancellationRequested();
                var entry = log.ReadEntry(index) ?? throw new InvalidOperationException(MissingEntry);
                entries.Add(entry);
                if (entry.Index != index || ReplicaProtocolCodec.MeasureEntries(entries) > limits.MaxBatchBytes)
                {
                    throw new InvalidOperationException(MissingEntry);
                }
            }
            return entries.SingleOrDefault(entry => entry.Operation?.Id == id)
                ?? throw new InvalidOperationException(MissingEntry);
        }
        finally
        {
            log.ProtocolGate.Release();
        }
    }

    internal EntityRef Reference(Guid creation, string collection, string id)
    {
        var expected = new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId,
            new(collection, ResourceKind.Collection, Partition.TransactionDomainId));
        var original = ReadRetainedEntry(creation).Operation
            ?? throw new InvalidOperationException(MissingEntry);
        var body = NativeCommandPayload.Read<ConfigureResourceRequest>(original);
        if (original.Id != creation || original.Kind != OperationKind.ConfigureResource
            || original.PrincipalId != ConnectionNativeProtocol.Root
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
