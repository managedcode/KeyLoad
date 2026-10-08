using KeyLoad.Core;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Owns one real configured movement node through the shared original process-native owner.</summary>
internal sealed class ControlledPartitionMovementNode : IDisposable
{
    private const string DirectoryPrefix = "keyload-controlled-movement-node-";
    private const string GuidFormat = "N";
    private readonly PhysicalShardRecord owner;
    private readonly string root = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
    private ControlledPartitionMovementNativeNode native;
    private bool closed;

    internal ControlledPartitionMovementNode(PhysicalShardRecord owner)
    {
        this.owner = owner;
        native = new(root, owner);
    }

    internal DatabaseEngine Database => native.Database;
    internal ZoneTreeStore Store => native.Store;
    internal ControlledPartitionMovementNativeJournal Journal => native.Journal;
    internal string Root => native.Root;

    internal void Reopen()
    {
        ObjectDisposedException.ThrowIf(closed, this);
        native.Dispose();
        native = new(root, owner);
    }

    public void Dispose()
    {
        if (closed)
        { return; }
        closed = true;
        var failures = new List<Exception>();
        try
        { native.Dispose(); }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        if (failures.Count == 0 && Directory.Exists(root))
        {
            try
            { Directory.Delete(root, recursive: true); }
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
            catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
