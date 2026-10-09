using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Only stopped, locked owned databases are copied; actual corrupt trees remain private original evidence.</summary>
internal sealed class PartitionMovementStoppedNativeSnapshot
{
    private const int EmptyInventoryCount = 0;
    private const string OriginalDirectory = "original";
    private const string CorruptDirectory = "corrupt";
    private const string DatabaseDirectory = "database";
    private const string ReplicaDirectory = "replica";
    private static readonly string[] NativeDirectories = [DatabaseDirectory, ReplicaDirectory];
    private readonly string evidenceRoot;
    private readonly Dictionary<string, NodeEpochRf3Inventory> originals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, UnixFileMode>> directoryModes = new(StringComparer.Ordinal);

    private PartitionMovementStoppedNativeSnapshot()
    {
        evidenceRoot = Path.Combine(Path.GetTempPath(), "keyload-retire-disposition-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidenceRoot);
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(evidenceRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
    }

    internal static async Task<PartitionMovementStoppedNativeSnapshot> CaptureAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3NativeCut[] stopped, CancellationToken cancellationToken)
    {
        RequireStopped(wave, stopped);
        var snapshot = new PartitionMovementStoppedNativeSnapshot();
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            foreach (var storage in NativeDirectories)
            {
                var source = Path.Combine(wave.OwnedDataRoot, node, storage);
                var inventory = await NodeEpochRf3Inventory.CaptureAsync(source, cancellationToken).ConfigureAwait(false);
                snapshot.originals.Add(Path.Combine(node, storage), inventory);
                snapshot.directoryModes.Add(Path.Combine(node, storage), CaptureModes(source, inventory));
            }
        }
        RequireAggregateBound(snapshot.originals.Values);
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            foreach (var storage in NativeDirectories)
            {
                var source = Path.Combine(wave.OwnedDataRoot, node, storage);
                var destination = Path.Combine(snapshot.evidenceRoot, OriginalDirectory, node, storage);
                var copied = await NodeEpochRf3Inventory.CopyTreeAsync(source, destination, cancellationToken).ConfigureAwait(false);
                RestoreModes(destination, snapshot.directoryModes[Path.Combine(node, storage)]);
                if (!snapshot.originals[Path.Combine(node, storage)].Equivalent(copied))
                { throw new IOException("The original stopped native cohort changed during snapshot capture."); }
            }
        }
        return snapshot;
    }

    internal async Task RestoreAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3NativeCut[] stopped,
        CancellationToken cancellationToken)
    {
        RequireStopped(wave, stopped);
        var failures = new List<Exception>();
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            foreach (var storage in NativeDirectories)
            { await ServerFailureObserver.ObserveAsync(() => RestoreNativeDirectoryAsync(wave, node, storage, cancellationToken), failures).ConfigureAwait(false); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task RestoreNativeDirectoryAsync(TwoRf3MembershipWave wave, string node, string storage, CancellationToken cancellationToken)
    {
        var destination = Path.Combine(wave.OwnedDataRoot, node, storage);
        var original = Path.Combine(evidenceRoot, OriginalDirectory, node, storage);
        var corrupt = Path.Combine(evidenceRoot, CorruptDirectory, node, storage);
        var originalInventory = await NodeEpochRf3Inventory.CaptureAsync(original, cancellationToken).ConfigureAwait(false);
        if (!originals[Path.Combine(node, storage)].Equivalent(originalInventory))
        { throw new IOException("The original stopped native snapshot changed before restore."); }
        RequireModes(original, directoryModes[Path.Combine(node, storage)]);
        var corruptInventory = await NodeEpochRf3Inventory.CaptureAsync(destination, cancellationToken).ConfigureAwait(false);
        var corruptModes = CaptureModes(destination, corruptInventory);
        Directory.CreateDirectory(Path.GetDirectoryName(corrupt)!);
        Directory.Move(destination, corrupt);
        var retained = await NodeEpochRf3Inventory.CaptureAsync(corrupt, cancellationToken).ConfigureAwait(false);
        RequireModes(corrupt, corruptModes);
        if (!corruptInventory.Equivalent(retained))
        { throw new IOException("The original corrupt native tree changed while retained."); }
        var restored = await NodeEpochRf3Inventory.CopyTreeAsync(original, destination, cancellationToken).ConfigureAwait(false);
        RestoreModes(destination, directoryModes[Path.Combine(node, storage)]);
        if (!originals[Path.Combine(node, storage)].Equivalent(restored))
        { throw new IOException("The restored native database differs from its original stopped snapshot."); }
    }

    private static Dictionary<string, UnixFileMode> CaptureModes(string root, NodeEpochRf3Inventory inventory)
    {
        var modes = new Dictionary<string, UnixFileMode>(StringComparer.Ordinal);
        if (OperatingSystem.IsWindows())
        { return modes; }
        modes.Add(string.Empty, File.GetUnixFileMode(root));
        foreach (var relative in inventory.Directories)
        { modes.Add(relative, File.GetUnixFileMode(ResolveKnownDirectory(root, relative))); }
        return modes;
    }

    private static void RestoreModes(string root, Dictionary<string, UnixFileMode> modes)
    {
        if (OperatingSystem.IsWindows())
        { return; }
        foreach (var pair in modes)
        { File.SetUnixFileMode(ResolveKnownDirectory(root, pair.Key), pair.Value); }
        RequireModes(root, modes);
    }

    private static void RequireModes(string root, Dictionary<string, UnixFileMode> modes)
    {
        if (OperatingSystem.IsWindows())
        { return; }
        foreach (var pair in modes)
        {
            if (File.GetUnixFileMode(ResolveKnownDirectory(root, pair.Key)) != pair.Value)
            { throw new IOException("The exact owned native directory mode changed."); }
        }
    }

    private static string ResolveKnownDirectory(string root, string relative)
    {
        if (relative.Length == EmptyInventoryCount)
        { return root; }
        var candidate = Path.GetFullPath(Path.Combine(root, relative));
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (Path.IsPathRooted(relative) || !candidate.StartsWith(prefix, StringComparison.Ordinal)
            || (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != EmptyInventoryCount)
        { throw new InvalidDataException("The captured native directory escaped its closed owned tree."); }
        return candidate;
    }

    private static void RequireAggregateBound(IEnumerable<NodeEpochRf3Inventory> inventories)
    {
        long bytes = EmptyInventoryCount;
        var files = EmptyInventoryCount;
        var directories = EmptyInventoryCount;
        foreach (var inventory in inventories)
        {
            bytes = checked(bytes + inventory.TotalBytes);
            files = checked(files + inventory.Files.Length);
            directories = checked(directories + inventory.Directories.Length);
        }
        if (bytes > NodeEpochRf3Protocol.MaximumInventoryBytes || files > NodeEpochRf3Protocol.MaximumFiles
            || directories > NodeEpochRf3Protocol.MaximumDirectories)
        { throw new InvalidDataException("The six stopped native databases exceed the central inventory bounds."); }
    }

    private static void RequireStopped(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3NativeCut[] stopped)
    {
        if (!stopped.Select(cut => cut.Node).Order(StringComparer.Ordinal)
            .SequenceEqual(TwoRf3MembershipProtocol.Nodes.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        { throw new InvalidOperationException("All six exact owned native stopped cuts are required."); }
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            var root = Path.Combine(wave.OwnedDataRoot, node);
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, "node.owner.lock"));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, DatabaseDirectory, "owner.lock"));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, ReplicaDirectory, "owner.lock"));
        }
    }
}
