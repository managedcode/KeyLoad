using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Retains the genuine completed directory outside publication, then restores the same exact stopped bytes.</summary>
internal static class ClusterRestoreRf3MissingSlot
{
    private const string RetainedDirectory = "missing-slot-original";
    private const string NativeOwner = "owner.lock";

    internal static async Task RequireAsync(ClusterRestoreRf3Fixture target, CancellationToken cancellationToken)
    {
        var original = Path.Combine(target.DataRoot, ClusterRestoreRf3Protocol.Nodes.First(),
            ClusterRestoreRf3Protocol.DatabaseDirectory);
        var retained = Path.Combine(ClusterRestoreRf3RetainedCut.OperationRoot(target), RetainedDirectory);
        if (!Directory.Exists(original) || Directory.Exists(retained) || File.Exists(retained)
            || (File.GetAttributes(original) & FileAttributes.ReparsePoint) != default)
        { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
        NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(original, NativeOwner));
        NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(original, ClusterRestoreRf3ResumeProtocol.SlotOwner));
        var failures = new List<Exception>();
        var moved = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            Directory.Move(original, retained);
            moved = true;
            var missingTarget = ClusterRestoreRf3RetainedCut.Target(target);
            var originalOperation = ClusterRestoreRf3RetainedCut.Operation(target);
            await target.StartRejectedMissingCompletedSlotAsync(cancellationToken).ConfigureAwait(false);
            await Assert.That(ClusterRestoreRf3RetainedCut.Target(target).SequenceEqual(missingTarget)).IsTrue();
            await Assert.That(ClusterRestoreRf3RetainedCut.Operation(target).SequenceEqual(originalOperation)).IsTrue();
            await Assert.That(Directory.Exists(original)).IsFalse();
        }, failures).ConfigureAwait(false);
        if (moved)
        { ServerFailureObserver.Observe(() => Directory.Move(retained, original), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
