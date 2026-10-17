using System.Collections.Immutable;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreRf3ExistingTargetRefusal
{
    private const int FirstNode = 0;
    private const int SiblingNode = 1;
    private const string RetainedSuffix = ".original-retained";

    internal static async Task RequireAsync(ClusterRestoreRf3Fixture target,
        ImmutableArray<ClusterBackupOwnerReceipt> originals, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var before = ClusterRestoreRf3RetainedCut.Target(target);
        await WrongNodeAsync(target).ConfigureAwait(false);
        await Assert.That(ClusterRestoreRf3RetainedCut.Target(target).SequenceEqual(before)).IsTrue();
        await MissingDirectoryAsync(target, cancellationToken).ConfigureAwait(false);
        await Assert.That(ClusterRestoreRf3RetainedCut.Target(target).SequenceEqual(before)).IsTrue();
        _ = await ClusterRestoreRf3TargetRead.RequireAsync(target, originals, dispatchPaused: true).ConfigureAwait(false);
    }

    private static async Task WrongNodeAsync(ClusterRestoreRf3Fixture target)
    {
        var failures = new List<Exception>();
        ZoneTreeStore? unexpected = null;
        KeyLoadException? refused = null;
        ServerFailureObserver.Observe(() =>
        {
            try
            {
                unexpected = ZoneTreeExistingStore.Open(new(ClusterRestoreRf3ExistingTarget.DirectoryPath(target, FirstNode))
                {
                    Incarnation = target.Mappings[FirstNode].Target.Incarnation
                }, target.OriginalTargetNodes[SiblingNode].NodeId, IntegrationExecutionOptions.StorageExecution());
            }
            catch (KeyLoadException failure)
            {
                refused = failure;
            }
        }, failures);
        if (unexpected is { } owned)
        {
            ServerFailureObserver.Observe(owned.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(refused is not null).IsTrue();
        var actual = refused ?? throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid);
        await Assert.That(actual.Code).IsEqualTo(ErrorCode.TokenInvalidated);
    }

    private static async Task MissingDirectoryAsync(ClusterRestoreRf3Fixture target,
        CancellationToken cancellationToken)
    {
        var directory = ClusterRestoreRf3ExistingTarget.DirectoryPath(target, FirstNode);
        var retained = directory + RetainedSuffix;
        if (Directory.Exists(retained) || File.Exists(retained))
        {
            throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid);
        }
        Directory.Move(directory, retained);
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var displaced = ClusterRestoreRf3RetainedCut.Target(target);
                DirectoryNotFoundException? refused = null;
                ZoneTreeStore? unexpected = null;
                try
                {
                    unexpected = ClusterRestoreRf3ExistingTarget.Open(target, FirstNode);
                }
                catch (DirectoryNotFoundException failure)
                {
                    refused = failure;
                }
                if (unexpected is { } owned)
                {
                    ServerFailureObserver.Observe(owned.Dispose, failures);
                }
                await Assert.That(refused is not null).IsTrue();
                await Assert.That(Directory.Exists(directory) || File.Exists(directory)).IsFalse();
                await Assert.That(ClusterRestoreRf3RetainedCut.Target(target).SequenceEqual(displaced)).IsTrue();
            }, failures).ConfigureAwait(false);
        }
        finally
        {
            ServerFailureObserver.Observe(() => Directory.Move(retained, directory), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
