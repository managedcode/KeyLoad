using System.Text.Json;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Replication;
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
        replica = new(new(Configuration.Directory)
        {
            Incarnation = Configuration.Incarnation,
            SigningKey = Canonical.Store.Identity.SigningKey
        }, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var configuration = ReplicaExecutionTestOptions.Configuration(Configuration);
        var execution = ReplicaExecutionTestOptions.Execution();
        Log = new(replica, configuration, canonicalDatabase: Canonical.Database);
        Materializer = CreateMaterializer(configuration, execution);
        Consensus = new(Materializer, configuration, execution, TimeProvider.System);
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
        try
        {
            await Consensus.DisposeAsync();
        }
        finally
        {
            try
            {
                await Materializer.DisposeAsync();
            }
            finally
            {
                Log.Dispose();
                replica.Dispose();
                Canonical.Dispose();
            }
        }
    }
}
