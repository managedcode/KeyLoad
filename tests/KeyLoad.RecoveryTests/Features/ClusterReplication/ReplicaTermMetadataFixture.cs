using KeyLoad.CrashHost;
using KeyLoad.Replication;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

internal sealed class ReplicaTermMetadataFixture : IDisposable
{
    internal const string VoterA = "term-node-a";
    internal const string VoterB = "term-node-b";
    internal const string VoterC = "term-node-c";
    internal const string TestDirectoryPrefix = "keyload-replica-term-metadata-";
    internal const int LargePayloadCharacters = 1_048_576;
    private readonly string directory;
    private readonly ZoneTreeStore store;
    private readonly DurableReplicaLog log;
    private bool disposed;

    internal ReplicaTermMetadataFixture(Action<CommitStage, long, int>? faultObserver = null)
    {
        directory = ReplicaFixturePaths.NewDirectory(TestDirectoryPrefix);
        Configuration = new(VoterA, [VoterA, VoterB, VoterC], directory, Guid.NewGuid());
        store = new(new ZoneTreeStoreOptions(directory)
        {
            Incarnation = Configuration.Incarnation,
            FaultObserver = faultObserver
        });
        try
        {
            log = new(store, Configuration);
        }
        catch
        {
            store.Dispose();
            Directory.Delete(directory, true);
            throw;
        }
    }

    internal ReplicaConfiguration Configuration { get; }
    internal ZoneTreeStore Store => store;
    internal DurableReplicaLog Log => log;
    internal string DirectoryPath => directory;

    internal static ReplicatedOperation Operation(string payload)
        => new(Guid.NewGuid(), OperationKind.Batch, VoterA, DateTimeOffset.UnixEpoch, payload);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        log.Dispose();
        store.Dispose();
        Directory.Delete(directory, true);
    }
}
