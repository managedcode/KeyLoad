using System.Text.Json;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Replication;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests;

internal sealed class ReplicaAppliedPositionWaitFixture : IAsyncDisposable
{
    private const string RootPrincipal = "root";
    private const string ReplicaKey = "replica";
    private const string BodyFormat = "{{\"value\":\"{0}\"}}";
    private readonly ZoneTreeStore replica;

    internal ReplicaAppliedPositionWaitFixture(DatabaseLimits? limits = null, TimeProvider? timeProvider = null)
    {
        Canonical = new TestDatabase(limits, timeProvider: timeProvider);
        Configuration = new(RootPrincipal, [RootPrincipal], Path.Combine(Canonical.Directory, ReplicaKey),
            Canonical.Store.Identity.Incarnation)
        { BenchmarkTopology = true };
        ZoneTreeStore? acquiredReplica = null;
        DurableReplicaLog? acquiredLog = null;
        ReplicaMaterializer? acquiredMaterializer = null;
        ReplicaConsensus? acquiredConsensus = null;
        try
        {
            replica = acquiredReplica = new(new(Configuration.Directory)
            {
                Incarnation = Configuration.Incarnation,
                SigningKey = Canonical.Store.Identity.SigningKey
            }, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
            var configuration = ReplicaExecutionTestOptions.Configuration(Configuration);
            var execution = ReplicaExecutionTestOptions.Execution();
            Log = acquiredLog = new(replica, configuration, canonicalDatabase: Canonical.Database);
            Materializer = acquiredMaterializer = CreateMaterializer(configuration, execution);
            Consensus = acquiredConsensus = new(Materializer, configuration, execution, TimeProvider.System);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            if (acquiredConsensus is not null)
            { ServerFailureObserver.ObserveAsync(() => acquiredConsensus.DisposeAsync().AsTask(), failures).GetAwaiter().GetResult(); }
            if (acquiredMaterializer is not null)
            { ServerFailureObserver.ObserveAsync(() => acquiredMaterializer.DisposeAsync().AsTask(), failures).GetAwaiter().GetResult(); }
            if (acquiredLog is not null)
            { ServerFailureObserver.Observe(acquiredLog.Dispose, failures); }
            if (acquiredReplica is not null)
            { ServerFailureObserver.Observe(acquiredReplica.Dispose, failures); }
            ServerFailureObserver.Observe(Canonical.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal TestDatabase Canonical { get; }
    internal ReplicaConfiguration Configuration { get; }
    internal DurableReplicaLog Log { get; }
    internal ReplicaMaterializer Materializer { get; private set; }
    internal ReplicaConsensus Consensus { get; }

    internal async Task RecreateMaterializerAsync()
    {
        await Materializer.DisposeAsync();
        Materializer = CreateMaterializer(ReplicaExecutionTestOptions.Configuration(Configuration),
            ReplicaExecutionTestOptions.Execution());
    }

    internal void ConfigureResource()
    {
        Canonical.Configure(ReplicaAppliedPositionWaitTests.Resource, ResourceKind.Collection);
    }

    private static readonly System.Text.CompositeFormat DocumentBodyFormat = System.Text.CompositeFormat.Parse(BodyFormat);

    internal void Commit(params string[] documentIds)
    {
        var entries = new List<ReplicaEntry>(documentIds.Length);
        var nextIndex = checked(Log.State.LastIndex + 1);
        if (Log.State.Term == 0)
        {
            Log.SaveTermAndVote(1, RootPrincipal);
        }
        foreach (var documentId in documentIds)
        {
            var commandId = Guid.NewGuid();
            var request = new CommandRequest(commandId, Canonical.Partition,
                [new PutDocument(ReplicaAppliedPositionWaitTests.Resource, documentId,
                    string.Format(System.Globalization.CultureInfo.InvariantCulture, DocumentBodyFormat, documentId))]);
            var operation = new ReplicatedOperation(commandId, OperationKind.Batch, RootPrincipal,
                TimeProvider.System.GetUtcNow(), JsonSerializer.Serialize(request, JsonDefaults.Options));
            entries.Add(new(nextIndex++, 1, Canonical.Database.NormalizeOperation(operation)));
        }
        Log.Append(entries);
        Materializer.Commit(nextIndex - 1);
    }

    internal void CommitCorruptedNoOpEntry()
    {
        var index = checked(Log.State.LastIndex + 1);
        if (Log.State.Term == 0)
        {
            Log.SaveTermAndVote(1, RootPrincipal);
        }
        Log.Append([new(index, Log.State.Term, null)]);
        replica.Commit((transaction, _) =>
        {
            transaction.Put(ReplicaProtocol.EntryStorageKey(index), new byte[] { byte.MaxValue });
            return true;
        });
        Materializer.Commit(index);
    }

    private ReplicaMaterializer CreateMaterializer(IOptions<ReplicaConfiguration> configuration,
        IOptions<ReplicaExecutionOptions> execution)
        => new(Canonical.Database, Log,
            new ReplicaSnapshotStore(Canonical.Store, Log, configuration, UnitExecutionOptions.ReplicaExecution()), execution);

    internal async Task AssertDocument(string documentId)
    {
        var record = Canonical.Store.Read(view => view.GetRecord<DocumentRecord>(DocumentStorageKeys.RecordKey(
            Canonical.Partition, ReplicaAppliedPositionWaitTests.Resource, documentId)));
        await Assert.That(record).IsNotNull();
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => Consensus.DisposeAsync().AsTask(), failures);
        await ServerFailureObserver.ObserveAsync(() => Materializer.DisposeAsync().AsTask(), failures);
        ServerFailureObserver.Observe(Log.Dispose, failures);
        try
        { replica.Dispose(); }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null) { failures.Add(error); }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null) { failures.Add(error); }
        ServerFailureObserver.Observe(Canonical.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
