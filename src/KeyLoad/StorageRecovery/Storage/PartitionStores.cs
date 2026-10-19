using System.Runtime.ExceptionServices;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Exclusive physical node ownership of canonical and independent replica storage.</summary>
internal sealed class PartitionStores : IDisposable
{
    private readonly FileStream ownership;
    private readonly Lock disposalGate = new();
    private bool closeAttempted;
    private ExceptionDispatchInfo? originalCloseFailure;
    private readonly PartitionHostOwnership settlement;
    private const string OriginalCloseUnjoined = "The original partition stores close has not returned; retain its root.";

    internal PartitionStores(IOptions<NodeOptions> nodeOptions, string directory,
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions, IOptions<ZoneTreePointCacheExecutionOptions> cacheOptions,
        TimeProvider? clock = null, Action<CommitStage, long, int>? canonicalObserver = null,
        PartitionHostOwnership? ownershipSettlement = null)
    {
        settlement = ownershipSettlement ?? new();
        var options = nodeOptions.Value;
        var rootExists = PartitionRootAdmission.InspectBeforeOwnership(directory);
        if (!rootExists)
        {
            Directory.CreateDirectory(directory);
        }

        ownership = new(Path.Combine(directory, PartitionStoreProtocol.OwnershipFile), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        settlement.NodeOwnershipAcquired();
        try
        {
            PartitionRootAdmission.InspectWhileOwned(directory);
            var canonicalOptions = StoreOptions(options, Path.Combine(directory, PartitionStoreProtocol.CanonicalDirectory), canonicalObserver);
            ZoneTreeStore.ValidateIdentityBeforeOpen(canonicalOptions, executionOptions);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            Canonical = ZoneTreeStore.OpenOwned(canonicalOptions, executionOptions, cacheOptions, settlement.Canonical, clock);
        }
        catch (Exception error)
        {
            var failures = new List<Exception> { error };
            CloseCanonical(failures);
            CloseOwnership(failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal ZoneTreeStore Canonical
    {
        get;
    }
    private static ZoneTreeStoreOptions StoreOptions(NodeOptions options, string directory,
        Action<CommitStage, long, int>? observer = null) => new(directory)
        {
            Incarnation = options.Incarnation,
            SigningKey = Convert.FromBase64String(options.SigningKey),
            FaultObserver = observer
        };

    public void Dispose()
    {
        lock (disposalGate)
        {
            if (closeAttempted)
            {
                originalCloseFailure?.Throw();
                if (!settlement.Canonical.IsReleased)
                {
                    throw new InvalidOperationException(OriginalCloseUnjoined);
                }
                return;
            }
            closeAttempted = true;
            var failures = new List<Exception>();
            CloseCanonical(failures);
            CloseOwnership(failures);
            try
            {
                ServerFailureObserver.ThrowIfAny(failures);
            }
            catch (Exception failure)
            {
                originalCloseFailure = ExceptionDispatchInfo.Capture(failure);
                throw;
            }
        }
    }

    private void CloseCanonical(List<Exception> failures)
    {
        if (Canonical is null)
        {
            return;
        }
        try
        {
            Canonical.Dispose();
        }
        catch (Exception failure) when (KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
    }

    private void CloseOwnership(List<Exception> failures)
    {
        try
        {
            ownership.Dispose();
            settlement.NodeOwnershipReleased();
        }
        catch (Exception failure) when (KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
    }
}
