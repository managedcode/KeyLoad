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
    private readonly string root = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
    private ControlledPartitionMovementNativeNode native;
    private bool closed;
    private IPartitionMovementCheckpointVerifier checkpointVerifier = UnavailablePartitionMovementCheckpointVerifier.Instance;

    internal ControlledPartitionMovementNode(PhysicalShardRecord owner,
        long maximumFixtureEntries = ControlledPartitionMovementNativeJournal.SupportingHistoryEntries)
    {
        PhysicalOwner = owner;
        MaximumFixtureEntries = maximumFixtureEntries;
        native = new(root, owner, maximumFixtureEntries: maximumFixtureEntries);
    }

    internal DatabaseEngine Database => native.Database;
    internal ZoneTreeStore Store => native.Store;
    internal ControlledPartitionMovementNativeJournal Journal => native.Journal;
    internal string Root => native.Root;
    internal PhysicalShardRecord PhysicalOwner { get; }
    internal long MaximumFixtureEntries { get; }
    internal bool RetainRoot { get; set; }

    internal void JoinForChild()
    {
        ObjectDisposedException.ThrowIf(closed, this);
        native.Dispose();
    }

    internal void OpenAfterChild()
    {
        ObjectDisposedException.ThrowIf(closed, this);
        native = new(root, PhysicalOwner, checkpointVerifier, maximumFixtureEntries: MaximumFixtureEntries);
    }

    internal void ReopenWithCheckpointVerifier(IPartitionMovementCheckpointVerifier verifier)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        ObjectDisposedException.ThrowIf(closed, this);
        native.Dispose();
        checkpointVerifier = verifier;
        native = new(root, PhysicalOwner, checkpointVerifier, maximumFixtureEntries: MaximumFixtureEntries);
    }

    internal void Reopen()
    {
        ObjectDisposedException.ThrowIf(closed, this);
        native.Dispose();
        native = new(root, PhysicalOwner, checkpointVerifier, maximumFixtureEntries: MaximumFixtureEntries);
    }

    internal KeyLoadException ReopenAfterExpectedCorruptApply()
    {
        ObjectDisposedException.ThrowIf(closed, this);
        KeyLoadException? original = null;
        try
        { native.Dispose(); }
        catch (KeyLoadException failure) when (failure.Code == ErrorCode.Corruption
            && failure.Message == KeyLoad.Core.Features.ClusterRouting.Contracts.PartitionMoveProtocol.Invalid)
        { original = failure; }
        if (original is null)
        { throw new InvalidOperationException("The original faulted apply worker did not retain its corruption."); }
        native = new(root, PhysicalOwner, checkpointVerifier, maximumFixtureEntries: MaximumFixtureEntries);
        return original;
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
        if (!RetainRoot && failures.Count == 0 && Directory.Exists(root))
        {
            try
            { Directory.Delete(root, recursive: true); }
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
            catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
