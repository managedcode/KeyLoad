using KeyLoad.Replication;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.Server;

/// <summary>Exclusive physical node ownership of canonical and independent replica storage.</summary>
internal sealed class PartitionStores : IDisposable
{
    private readonly FileStream ownership;
    private int disposed;

    internal PartitionStores(NodeOptions options, string directory)
    {
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        if (File.Exists(Path.Combine(directory, PartitionStoreProtocol.LegacyVotersFile))
            || Directory.Exists(Path.Combine(directory, PartitionStoreProtocol.LegacyLogDirectory)))
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, PartitionStoreProtocol.LegacyDirectory);
        }

        ownership = new(Path.Combine(directory, PartitionStoreProtocol.OwnershipFile), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        ZoneTreeStore? canonical = null;
        try
        {
            Canonical = canonical = Open(options, Path.Combine(directory, PartitionStoreProtocol.CanonicalDirectory));
            Replica = Open(options, Path.Combine(directory, ReplicaProtocol.ReplicaDirectory));
        }
        catch (Exception error)
        {
            var failures = new List<Exception> { error };
            if (canonical is not null)
            { ServerFailureObserver.Observe(() => canonical.Dispose(), failures); }
            ServerFailureObserver.Observe(() => ownership.Dispose(), failures);
            if (failures.Count > 1)
            { throw new AggregateException(failures); }
            throw;
        }
    }

    internal ZoneTreeStore Canonical { get; }
    internal ZoneTreeStore Replica { get; }

    private static ZoneTreeStore Open(NodeOptions options, string directory) => new(new(directory)
    {
        Incarnation = options.Incarnation,
        SigningKey = Convert.FromBase64String(options.SigningKey)
    });

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        { return; }
        var failures = new List<Exception>();
        var closingStores = CloseStoresAsync(failures);
        ServerFailureObserver.ObserveAsync(() => closingStores, failures).GetAwaiter().GetResult();
        var releasingOwnership = ReleaseOwnershipAsync(closingStores);
        ServerFailureObserver.ObserveAsync(() => releasingOwnership, failures).GetAwaiter().GetResult();
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task CloseStoresAsync(List<Exception> failures)
    {
        await ServerFailureObserver.ObserveAsync(() => { Replica.Dispose(); return Task.CompletedTask; }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => { Canonical.Dispose(); return Task.CompletedTask; }, failures).ConfigureAwait(false);
    }

    private async Task ReleaseOwnershipAsync(Task closingStores)
    {
        await closingStores.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        await ownership.DisposeAsync().ConfigureAwait(false);
    }
}

internal static class PartitionStoreProtocol
{
    internal const string CanonicalDirectory = "database";
    internal const string OwnershipFile = "node.owner.lock";
    internal const string LegacyVotersFile = "voters.bin";
    internal const string LegacyLogDirectory = "raft";
    internal const string AdministratorId = "root";
    internal const string AdministratorTenant = "system";
    internal const string Wildcard = "*";
    internal const string LegacyDirectory = "The directory contains an obsolete consensus format. Use a separate new cluster directory.";
}
