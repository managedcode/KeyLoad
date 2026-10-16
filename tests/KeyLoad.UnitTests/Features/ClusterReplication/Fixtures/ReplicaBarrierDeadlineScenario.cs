using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class ReplicaBarrierDeadlineScenario
{
    private readonly Lock resourceCreationGate = new();
    private readonly Guid resourceCreationId = Guid.NewGuid();
    private ReplicatedOperation? resourceCreation;
    internal const int CanonicalKeyBytes = 32;
    internal const string Root = "root";
    internal const string Collection = "barrier-deadline";
    internal const string DocumentId = "committed";
    internal const string Json = "{\"value\":\"after-owned-admission\"}";
    private const string PartitionKey = "barrier";
    private const string Credential = "barrier.deadline-test-credential-32-characters";
    internal static readonly PartitionRef Partition = new("tenant", "database", "orders", PartitionKey);
    internal static readonly TimeSpan RpcTimeout = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan LowerElectionTimeout = TimeSpan.FromSeconds(4);
    internal static readonly TimeSpan UpperElectionTimeout = TimeSpan.FromSeconds(8);
    internal static readonly TimeSpan Hold = TimeSpan.FromSeconds(3);
    private readonly TaskCompletionSource flushed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource firstBarrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource secondBarrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static readonly TimeSpan ReceiverCommandTimeout = TimeSpan.FromSeconds(5);
    internal bool ReceiverDeadline
    {
        get;
    }
    internal ReplicaExecutionOptions? ExecutionSettings => ReceiverDeadline
        ? new()
        {
            CommandTimeout = ReceiverCommandTimeout
        } : null;
    internal ReplicaBarrierDeadlineScenario(bool receiverDeadline = false) => ReceiverDeadline = receiverDeadline;
    internal void WatchReceiver(string voter) => leader = voter;
    private string? leader;
    private int armed;
    private int arrivals;
    internal Task Flushed => flushed.Task;
    internal Task FirstBarrier => firstBarrier.Task;
    internal Task SecondBarrier => secondBarrier.Task;
    internal void Release() => released.TrySetResult();

    internal static ReplicaConfiguration Configuration(string voter,
        System.Collections.Immutable.ImmutableArray<string> voters, Guid incarnation, bool selected)
        => new(voter, voters, Path.GetTempPath(), incarnation)
        {
            RpcTimeout = selected ? RpcTimeout : RequestCqrsCohortScenario.RpcTimeout,
            LowerElectionTimeout = selected ? LowerElectionTimeout : RequestCqrsCohortScenario.LowerElectionTimeout,
            UpperElectionTimeout = selected ? UpperElectionTimeout : RequestCqrsCohortScenario.UpperElectionTimeout
        };

    internal void Arm(string voter)
    {
        leader = voter;
        Volatile.Write(ref armed, 1);
    }

    internal Action<CommitStage, long, int> Observer(string voter) => (stage, _, _1) =>
    {
        if (stage != CommitStage.JournalFlushed || voter != leader
            || Interlocked.CompareExchange(ref armed, 0, 1) != 1)
        {
            return;
        }
        flushed.TrySetResult();
        released.Task.WaitAsync(RequestCqrsCohortRuntimeFixture.ShutdownBound).GetAwaiter().GetResult();
    };

    internal ReplicatedOperation Prepare(DatabaseEngine database, ReplicaConfiguration configuration)
    {
        configuration.Validate();
        if (configuration.Incarnation != database.Store.Identity.Incarnation)
        {
            throw new InvalidOperationException("The native barrier cohort and canonical store incarnation differ.");
        }
        TestDatabaseEngineFactory.Bootstrap(database, Root, Credential, physicalCatalog: false);
        PhysicalShardTestBootstrap.Bootstrap(database, Root, configuration.VoterIds);
        // Every voter applies the same retained original creation; its fingerprint owns the resource lifetime.
        var original = ResourceCreation(database);
        _ = database.Apply(original).Get<ResourceDefinition>();
        return original;
    }

    private ReplicatedOperation ResourceCreation(DatabaseEngine database)
    {
        lock (resourceCreationGate)
        {
            if (resourceCreation is null)
            {
                var request = new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId,
                    new(Collection, ResourceKind.Collection, Partition.TransactionDomainId));
                // This pre-log baseline must precede the original history when a fresh peer catches up.
                resourceCreation = database.CreateNativeOperation(OperationKind.ConfigureResource, resourceCreationId, Root,
                    DateTimeOffset.UnixEpoch, NativeSerialization.Serialize(request));
            }
            return resourceCreation;
        }
    }

    internal IReplicaTransportObservation Observe(string voter) => new Incoming(this, voter);

    private sealed class Incoming(ReplicaBarrierDeadlineScenario owner, string voter) : IReplicaTransportObservation
    {
        public Action<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>? BeginIncoming(ReplicaRpc method)
        {
            if (voter == owner.leader && (owner.ReceiverDeadline || owner.flushed.Task.IsCompleted) && !owner.released.Task.IsCompleted
                && method is ReplicaRpc.ReadBarrier or ReplicaRpc.ControlReadBarrier)
            {
                var count = Interlocked.Increment(ref owner.arrivals);
                if (count == 1)
                {
                    owner.firstBarrier.TrySetResult();
                }
                else if (count == 2)
                {
                    owner.secondBarrier.TrySetResult();
                }
            }
            return null;
        }
    }
}
