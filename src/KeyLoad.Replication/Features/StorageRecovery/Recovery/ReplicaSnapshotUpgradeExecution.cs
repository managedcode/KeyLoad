using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Replication;

internal static class ReplicaSnapshotUpgradeExecution
{
    internal const string DestinationConflict = "The replica snapshot conversion destination must be absent.";
    private const string InvalidOutput = "The converted replica image does not preserve its source logical cut.";

    internal static void ConvertAndPublish(ReplicaSnapshotUpgradePlan plan, DatabaseEngine canonical,
        IAtomicStore replica, ReplicaConfiguration configuration, string destination,
        Func<string, string, StorageSnapshot> convertImage)
    {
        Directory.CreateDirectory(destination);
        var created = new List<string>();
        var beforeReplicaPosition = replica.Position;
        var beforeCanonicalPosition = canonical.Store.Position;
        var beforeApplied = ReplicaSnapshotUpgradeValidation.ReadAppliedPosition(canonical.Store);
        var preserve = false;
        try
        {
            ConvertImages(plan, canonical, configuration, destination, convertImage, created);
            VerifyOutputInventory(destination, plan.Images);
            ReplicaSnapshotUpgradeValidation.Revalidate(plan, canonical, replica, configuration);
            preserve = plan.HardState.Snapshot is not null;
            PublishPointer(plan, canonical, replica, configuration, destination, beforeReplicaPosition);
            using var log = new DurableReplicaLog(replica, configuration, canonicalDatabase: canonical);
            new ReplicaSnapshotStore(canonical.Store, log, configuration).Recover();
            VerifyPostState(plan, canonical, replica, log, beforeReplicaPosition, beforeCanonicalPosition, beforeApplied);
        }
        catch (Exception primary)
        {
            if (!preserve)
            { CleanupUnpublished(destination, created, primary); }
            ExceptionDispatchInfo.Capture(primary).Throw();
            throw;
        }
    }

    internal static bool Overlaps(string first, string second)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var separator = Path.DirectorySeparatorChar.ToString();
        var firstPath = Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar) + separator;
        var secondPath = Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar) + separator;
        return firstPath.StartsWith(secondPath, comparison) || secondPath.StartsWith(firstPath, comparison);
    }

    private static void ConvertImages(ReplicaSnapshotUpgradePlan plan, DatabaseEngine canonical,
        ReplicaConfiguration configuration, string destination, Func<string, string, StorageSnapshot> convertImage,
        List<string> created)
    {
        long totalBytes = 0;
        foreach (var image in plan.Images)
        {
            var source = Path.Combine(plan.SourceSnapshots, image.FileName);
            var output = Path.Combine(destination, image.FileName);
            var reported = convertImage(source, output);
            created.Add(output);
            VerifyOutput(canonical, output, configuration, image, reported);
            var length = new FileInfo(output).Length;
            if (length > ReplicaSnapshotUpgradeInventory.MaximumTotalBytes - totalBytes)
            { throw Errors.Fail(ErrorCode.ResourceExhausted, InvalidOutput); }
            totalBytes += length;
        }
    }

    private static void VerifyOutput(DatabaseEngine canonical, string output, ReplicaConfiguration configuration,
        ReplicaSnapshotUpgradeImage image, StorageSnapshot reported)
    {
        ReplicaSnapshotUpgradeInventory.RejectLinks(output);
        var info = new FileInfo(output);
        if (!info.Exists || info.Length <= 0 || info.Length > configuration.MaxSnapshotBytes
            || !SameCut(image.Snapshot, reported))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidOutput); }
        var verified = canonical.Store.VerifySnapshot(output);
        if (!SameCut(image.Snapshot, verified))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidOutput); }
    }

    private static void PublishPointer(ReplicaSnapshotUpgradePlan plan, DatabaseEngine canonical,
        IAtomicStore replica, ReplicaConfiguration configuration, string destination, long beforePosition)
    {
        if (plan.HardState.Snapshot is not { } source)
        { return; }
        var path = Path.Combine(destination, source.FileName);
        var snapshot = source with
        {
            Length = new FileInfo(path).Length,
            Sha256 = ReplicaSnapshotUpgradeInventory.Digest(path, configuration.MaxSnapshotBytes)
        };
        if (snapshot.Length == source.Length && snapshot.Sha256 == source.Sha256)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidOutput); }
        using var log = new DurableReplicaLog(replica, configuration, canonicalDatabase: canonical);
        log.PublishSnapshot(snapshot);
        if (replica.Position != checked(beforePosition + 1)
            || log.State != (plan.HardState with { Snapshot = snapshot }))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidOutput); }
    }

    private static void VerifyPostState(ReplicaSnapshotUpgradePlan plan, DatabaseEngine canonical,
        IAtomicStore replica, DurableReplicaLog log, long beforeReplicaPosition, long beforeCanonicalPosition,
        long beforeApplied)
    {
        var expected = plan.HardState;
        var expectedPosition = checked(beforeReplicaPosition + (expected.Snapshot is null ? 0 : 1));
        var replicaBinding = ReplicaSnapshotUpgradeValidation.Bind(replica);
        var expectedReplica = plan.Replica with { Position = expectedPosition };
        if (replicaBinding != expectedReplica || ReplicaSnapshotUpgradeValidation.Bind(canonical.Store) != plan.Canonical
            || replica.Position != expectedPosition || canonical.Store.Position != beforeCanonicalPosition
            || ReplicaSnapshotUpgradeValidation.ReadAppliedPosition(canonical.Store) != beforeApplied
            || log.State.Term != expected.Term || log.State.VotedFor != expected.VotedFor
            || log.State.LastIndex != expected.LastIndex || log.State.CommittedIndex != expected.CommittedIndex)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidOutput); }
        VerifyPointer(expected.Snapshot, log.State.Snapshot);
    }

    private static void VerifyPointer(ReplicaSnapshot? expected, ReplicaSnapshot? actual)
    {
        if (expected is null)
        {
            if (actual is not null)
            { throw Errors.Fail(ErrorCode.Corruption, InvalidOutput); }
            return;
        }
        var restored = actual is null ? null : actual with { Length = expected.Length, Sha256 = expected.Sha256 };
        if (restored != expected)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidOutput); }
    }

    private static void VerifyOutputInventory(string destination,
        ImmutableArray<ReplicaSnapshotUpgradeImage> images)
    {
        var expected = images.Select(image => image.FileName).ToHashSet(StringComparer.Ordinal);
        var entries = 0;
        foreach (var path in Directory.EnumerateFileSystemEntries(destination))
        {
            if (++entries > ReplicaSnapshotUpgradeInventory.MaximumImages)
            { throw Errors.Fail(ErrorCode.ResourceExhausted, InvalidOutput); }
            ReplicaSnapshotUpgradeInventory.RejectLinks(path);
            if (Directory.Exists(path) || !expected.Remove(Path.GetFileName(path)))
            { throw Errors.Fail(ErrorCode.FormatUnsupported, InvalidOutput); }
        }
        if (expected.Count != 0)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidOutput); }
    }

    private static bool SameCut(StorageSnapshot first, StorageSnapshot second)
        => first.Incarnation == second.Incarnation && first.Position == second.Position
            && first.AppliedPosition == second.AppliedPosition && first.RecordCount == second.RecordCount;

    private static void CleanupUnpublished(string destination, List<string> created, Exception primary)
    {
        try
        {
            foreach (var path in created)
            { File.Delete(path); }
            if (Directory.Exists(destination))
            {
                if (Directory.EnumerateFileSystemEntries(destination).Any())
                { throw new IOException("The private snapshot destination contains unowned cleanup artifacts."); }
                Directory.Delete(destination);
            }
        }
        catch (Exception cleanup)
        { throw new AggregateException("Replica snapshot upgrade failed and cleanup was incomplete.", primary, cleanup); }
    }
}
