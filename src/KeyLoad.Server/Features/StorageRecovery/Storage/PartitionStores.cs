using KeyLoad.Replication;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Exclusive physical node ownership of canonical and independent replica storage.</summary>
internal sealed class PartitionStores : IDisposable
{
    private readonly FileStream ownership;
    private int disposed;

    internal PartitionStores(IOptions<NodeOptions> nodeOptions, string directory,
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions, IOptions<ZoneTreePointCacheExecutionOptions> cacheOptions,
        TimeProvider? clock = null, Action<CommitStage, long, int>? canonicalObserver = null)
    {
        const int FailuresCountValidationBoundary = 1;

        var options = nodeOptions.Value;
        var rootExists = PartitionRootAdmission.InspectBeforeOwnership(directory);
        if (!rootExists)
        {
            Directory.CreateDirectory(directory);
        }

        ownership = new(Path.Combine(directory, PartitionStoreProtocol.OwnershipFile), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        ZoneTreeStore? canonical = null;
        try
        {
            PartitionRootAdmission.InspectWhileOwned(directory);
            var canonicalOptions = StoreOptions(options, Path.Combine(directory, PartitionStoreProtocol.CanonicalDirectory), canonicalObserver);
            var replicaOptions = StoreOptions(options, Path.Combine(directory, ReplicaProtocol.ReplicaDirectory));
            ZoneTreeStore.ValidateIdentityBeforeOpen(canonicalOptions, executionOptions);
            ZoneTreeStore.ValidateIdentityBeforeOpen(replicaOptions, executionOptions);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            Canonical = canonical = new(canonicalOptions, executionOptions, cacheOptions, clock);
            Replica = new(replicaOptions, executionOptions, cacheOptions, clock);
        }
        catch (Exception error)
        {
            var failures = new List<Exception> { error };
            if (canonical is not null)
            { ServerFailureObserver.Observe(() => canonical.Dispose(), failures); }
            ServerFailureObserver.Observe(() => ownership.Dispose(), failures);
            if (failures.Count > FailuresCountValidationBoundary)
            { throw new AggregateException(failures); }
            throw;
        }
    }

    internal ZoneTreeStore Canonical { get; }
    internal ZoneTreeStore Replica { get; }

    private static ZoneTreeStoreOptions StoreOptions(NodeOptions options, string directory,
        Action<CommitStage, long, int>? observer = null) => new(directory)
    {
        Incarnation = options.Incarnation,
        SigningKey = Convert.FromBase64String(options.SigningKey),
        FaultObserver = observer
    };

    public void Dispose()
    {
        const int ValueSingleItemCount = 1;
        const int EmptyExchange = 0;

        if (Interlocked.Exchange(ref disposed, ValueSingleItemCount) != EmptyExchange)
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
    internal const string AdministratorId = "root";
    internal const string AdministratorTenant = "system";
    internal const string Wildcard = "*";
}
