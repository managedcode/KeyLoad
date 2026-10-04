using KeyLoad.CrashHost;
using KeyLoad.Replication;

namespace KeyLoad.RecoveryTests.Features.ClusterReplication;

/// <summary>Creates genuine closed CrashHost ZoneTree stores and holds their existing files exclusively.</summary>
internal sealed class ReplicaFileReadinessStores : IAsyncDisposable
{
    private const string DirectoryPrefix = "keyload-replica-file-readiness-";
    internal static readonly string MetadataWal = Path.Combine("tree", "0.meta.wal");
    private FileStream? heldFile;

    internal ReplicaFileReadinessStores(ReplicaCrashBoundary boundary)
    {
        var (root, _) = CreateClosedStores(boundary);
        Root = root;
    }

    internal string Root { get; }

    internal void HoldTargetFile(string store, string file) => Hold(TargetDirectory(store), file);

    internal void HoldSourceFile(string store, string file) => Hold(SourceDirectory(store), file);

    internal void ReleaseHeldFile()
    {
        heldFile?.Dispose();
        heldFile = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (heldFile is not null)
        {
            await heldFile.DisposeAsync();
            heldFile = null;
        }
    }

    internal Task DeleteRootAsync(CancellationToken cancellationToken)
        => ReplicaProcessFiles.DeleteAsync(Root, cancellationToken);

    private void Hold(string directory, string file)
        => heldFile = new(Path.Combine(directory, file), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

    private string TargetDirectory(string store) => store switch
    {
        "canonical" => ReplicaCrashNode.TargetStoreDirectories(Root)[0],
        "replica" => ReplicaCrashNode.TargetStoreDirectories(Root)[1],
        _ => throw new ArgumentOutOfRangeException(nameof(store))
    };

    private string SourceDirectory(string store) => store switch
    {
        "canonical" => ReplicaCrashNode.SourceStoreDirectories(Root)[0],
        "replica" => ReplicaCrashNode.SourceStoreDirectories(Root)[1],
        _ => throw new ArgumentOutOfRangeException(nameof(store))
    };

    private static (string Root, Guid Incarnation) CreateClosedStores(ReplicaCrashBoundary boundary)
    {
        var root = ReplicaFixturePaths.NewDirectory(DirectoryPrefix);
        var incarnation = Guid.NewGuid();
        try
        {
            using (ReplicaCrashNode.OpenTarget(root, incarnation))
            {
            }

            if (ReplicaCrashNode.RequiresSourceStores(boundary))
            {
                using var source = ReplicaCrashNode.OpenSource(root, incarnation);
                source.Populate(4);
                _ = source.Snapshots.Create(4, 1);
            }

            return (root, incarnation);
        }
        catch (Exception)
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            throw;
        }
    }
}
