using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Reconstitutes the genuinely joined original source, never restarting resources from a disposed graph.</summary>
internal static class ClusterRestoreRf3ColdCapture
{
    internal static async Task RequireAsync(TwoRf3MembershipWave source,
        PartitionMovementPublicParentRf3Seed seed, ImmutableArray<ClusterBackupOwnerReceipt> originals,
        ImmutableArray<string> archives, CancellationToken cancellationToken)
    {
        await source.RestartJoinedAsync(cancellationToken).ConfigureAwait(false);
        var actual = await ClusterRestoreRf3Capture.CaptureAsync(source, seed.Partition,
            originals.First().Cut.CaptureId, cancellationToken).ConfigureAwait(false);
        foreach (var original in originals)
        {
            await SqlRf3Protocol.EqualAsync(original, actual.Single(receipt => receipt.Cut.Owner.PhysicalShardId
                == original.Cut.Owner.PhysicalShardId));
        }
        await seed.VerifyAsync(cancellationToken).ConfigureAwait(false);
        _ = await source.StopForDirectoryReadAsync().ConfigureAwait(false);
        await ClusterRestoreRf3NativeArchive.RequireOriginalAsync(originals, archives, cancellationToken).ConfigureAwait(false);
    }
}
