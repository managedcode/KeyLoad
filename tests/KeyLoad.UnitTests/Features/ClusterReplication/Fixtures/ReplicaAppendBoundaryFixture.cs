using KeyLoad.Replication;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests;

internal sealed class ReplicaAppendBoundaryFixture : IDisposable
{
    internal const string Resource = "append-boundary-documents";
    private const string Voter = "append-boundary-a";
    internal TestDatabase Canonical { get; } = new();
    internal ZoneTreeStore Store { get; }
    internal DurableReplicaLog Log { get; private set; }
    internal ReplicaConfiguration Configuration { get; }
    private readonly Action<ReplicaCrashBoundary>? observer;

    internal ReplicaAppendBoundaryFixture(Action<ReplicaCrashBoundary>? observer = null)
    {
        this.observer = observer;
        Configuration = new(Voter, [Voter, "append-boundary-b", "append-boundary-c"],
            Path.Combine(Canonical.Directory, "boundary-replica"), Canonical.Store.Identity.Incarnation);
        ZoneTreeStore? acquiredStore = null;
        DurableReplicaLog? acquiredLog = null;
        try
        {
            Canonical.Configure(Resource, ResourceKind.Collection);
            Store = acquiredStore = new(new(Configuration.Directory)
            { Incarnation = Configuration.Incarnation, SigningKey = Canonical.Store.Identity.SigningKey },
                UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
            Log = acquiredLog = Open();
            Log.SaveTermAndVote(1, Voter);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            if (acquiredLog is not null)
            { ServerFailureObserver.Observe(acquiredLog.Dispose, failures); }
            if (acquiredStore is not null)
            { ServerFailureObserver.Observe(acquiredStore.Dispose, failures); }
            ServerFailureObserver.Observe(Canonical.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal ReplicaEntry Create(string document, long index)
    {
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, Canonical.Partition,
            [new PutDocument(Resource, document, "{\"value\":1}")]);
        var operation = Canonical.Database.CreateNativeOperation(OperationKind.Batch, id, "root",
            TimeProvider.System.GetUtcNow(), NativeSerialization.Serialize(request));
        return new(index, Log.State.Term, operation);
    }

    private DurableReplicaLog Open() => new(Store, UnitExecutionOptions.ReplicaConfiguration(Configuration),
        observer, Canonical.Database);

    internal void Reopen()
    {
        Log.Dispose();
        Log = Open();
    }

    public void Dispose()
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(Log.Dispose, failures);
        ServerFailureObserver.Observe(Store.Dispose, failures);
        ServerFailureObserver.Observe(Canonical.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
