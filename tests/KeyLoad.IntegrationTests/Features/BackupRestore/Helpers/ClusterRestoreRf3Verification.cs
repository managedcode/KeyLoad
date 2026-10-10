using System.Collections.Immutable;
using System.Globalization;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreRf3Verification
{
    private const string RtoLabel = "Actual whole CLI publication/target startup/public verification/cold continuation RTO: ";
    private const string DurationFormat = "c";

    internal static async Task RunAsync(ClusterRestoreRf3Fixture target,
        PartitionMovementPublicParentRf3Seed seed, ImmutableArray<ClusterBackupOwnerReceipt> originals,
        ClusterRestoreRf3EventingState eventing, ClusterRestoreRf3SecondaryState secondary,
        IReadOnlyList<string>? invalidCredentials, ImmutableArray<string> repairedArchives, ClusterRestoreMixedRetentionRf3State? mixed,
        string rootKey, CancellationToken cancellationToken)
    {
        var restoreStarted = TimeProvider.System.GetTimestamp();
        await target.StartDerivativeArchiveAsync(repairedArchives, rejectModified: false, cancellationToken).ConfigureAwait(false);
        var nativeNodes = await ClusterRestoreRf3TargetRead.RequireAsync(target, originals,
            dispatchPaused: true).ConfigureAwait(false);
        await ClusterRestoreRf3CanonicalCut.RequireOriginalAsync(target, originals, repairedArchives,
            cancellationToken).ConfigureAwait(false);
        if (mixed is not null)
        { ClusterRestoreMixedRetentionRf3NativeCut.RequireRetained(target, mixed, cancellationToken); }
        await target.RequireOperatorReceiptAsync(nativeNodes).ConfigureAwait(false);
        await Assert.That(target.OriginalOperatorExits).IsEquivalentTo(new[] { ClusterRestoreRf3Protocol.FailedExit, ClusterRestoreRf3Protocol.FailedExit,
            ClusterRestoreRf3Protocol.FailedExit, ClusterRestoreRf3Protocol.SuccessfulExit },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await target.StartAsync(restore: false, cancellationToken).ConfigureAwait(false);
        if (invalidCredentials is not null)
        {
            await ClusterRestoreRf3CredentialTrial.RequireTargetAsync(target, seed.Partition, invalidCredentials,
            cancellationToken).ConfigureAwait(false);
        }
        await ClusterRestoreRf3SecondaryPartition.RequireTargetAsync(target, seed.Credential, secondary, originals, cancellationToken).ConfigureAwait(false);
        if (mixed is not null)
        { await ClusterRestoreMixedRetentionRf3Restore.PausedAsync(target, mixed, cancellationToken).ConfigureAwait(false); }
        var fresh = await ClusterRestoreRf3Scenario.CallersAsync(target, seed, originals, eventing, null, cancellationToken).ConfigureAwait(false);
        if (mixed is not null)
        { await ClusterRestoreMixedRetentionRf3Restore.ContinueAsync(target, mixed, rootKey, cancellationToken).ConfigureAwait(false); }
        _ = await ClusterRestoreRf3TargetRead.RequireAsync(target, originals, dispatchPaused: false).ConfigureAwait(false);
        var finalCut = ClusterRestoreRf3CanonicalCut.Target(target, originals, cancellationToken);
        await target.StartAsync(restore: false, cancellationToken).ConfigureAwait(false);
        if (invalidCredentials is not null)
        {
            await ClusterRestoreRf3CredentialTrial.RequireTargetAsync(target, seed.Partition, invalidCredentials,
            cancellationToken).ConfigureAwait(false);
        }
        await ClusterRestoreRf3SecondaryPartition.RequireTargetAsync(target, seed.Credential, secondary, originals, cancellationToken).ConfigureAwait(false);
        _ = await ClusterRestoreRf3Scenario.CallersAsync(target, seed, originals, eventing, fresh, cancellationToken).ConfigureAwait(false);
        if (mixed is not null)
        { await ClusterRestoreMixedRetentionRf3Cold.RequireAsync(target, mixed, cancellationToken).ConfigureAwait(false); }
        await target.StopAsync().ConfigureAwait(false);
        if (mixed is not null)
        { ClusterRestoreMixedRetentionRf3NativeCut.RequireRetained(target, mixed, cancellationToken); }
        await ClusterRestoreRf3CanonicalCut.RequireColdAsync(target, originals, finalCut, cancellationToken).ConfigureAwait(false);
        var elapsed = TimeProvider.System.GetElapsedTime(restoreStarted);
        await Assert.That(elapsed).IsLessThan(ClusterRestoreRf3Protocol.ParentDeadline);
        await TestContext.Current!.OutputWriter.WriteLineAsync(RtoLabel + elapsed.ToString(DurationFormat,
            CultureInfo.InvariantCulture));
    }

}
