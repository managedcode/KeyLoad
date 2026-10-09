using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Actual incompatible movement cuts cannot publish; fresh coherent cuts restore the moved corpus.</summary>
internal static class ClusterRestoreRf3AuthorityTrialVector
{
    private const string InconsistentArchives = "split-off-node";
    private const string RejectedTarget = "split-target";
    private const string HealthyTarget = "fresh-target";

    internal static async Task RunAsync(TwoRf3MembershipWave source, PartitionMovementPublicParentRf3Seed seed,
        string ownedRoot, CancellationToken cancellationToken)
    {
        var capture = Guid.NewGuid();
        var before = await ClusterRestoreRf3Capture.CaptureOneAsync(source, seed.Partition, capture,
            seed.Directory.ControlOwner, TwoRf3MembershipProtocol.Node1, cancellationToken).ConfigureAwait(false);
        var actual = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.MovePartitionAsync(seed.FirstRequest,
            cancellationToken).ConfigureAwait(false));
        await PartitionMovementPublicParentRf3Scenario.RequireTerminalAsync(seed, seed.FirstRequest,
            seed.OriginalPlacement, seed.Directory.ControlOwner, actual, cancellationToken).ConfigureAwait(false);
        await seed.VerifyAsync(cancellationToken).ConfigureAwait(false);
        var owner = seed.Directory.Owners.Single(entry => entry.Owner.PhysicalShardId
            == seed.FirstRequest.DestinationPhysicalShardId).Owner;
        var after = await ClusterRestoreRf3Capture.CaptureOneAsync(source, seed.Partition, capture,
            owner, TwoRf3MembershipProtocol.Node4, cancellationToken).ConfigureAwait(false);
        await Assert.That(before.Cut.CaptureId).IsEqualTo(after.Cut.CaptureId);
        var left = before.Cut.Partitions.Single(partition => partition.Roster.Partition == seed.Partition);
        var right = after.Cut.Partitions.Single(partition => partition.Roster.Partition == seed.Partition);
        await Assert.That(left.Placement.PhysicalShardId).IsNotEqualTo(right.Placement.PhysicalShardId);
        await Assert.That(right.CanonicalRecordCount).IsGreaterThan(0L);
        var originals = System.Collections.Immutable.ImmutableArray.Create(before, after);
        var archives = await ClusterRestoreRf3NativeArchive.CopyAsync(source, originals,
            Path.Combine(ownedRoot, InconsistentArchives), cancellationToken).ConfigureAwait(false);
        var rejected = new ClusterRestoreRf3Fixture(Path.Combine(ownedRoot, RejectedTarget), source.Profile.AdminKey,
            originals, archives);
        Exception? initiatingFailure = null;
        var cleanupCompleted = false;
        try
        {
            try
            {
                await rejected.StartRejectedAsync(ErrorCode.RecoveryRequired,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception original)
            {
                initiatingFailure = original;
                throw;
            }
            finally { await rejected.DisposeAsync().ConfigureAwait(false); cleanupCompleted = true; }
        }
        catch (Exception terminal)
        {
            if (initiatingFailure is not null && !cleanupCompleted)
            { throw new AggregateException(initiatingFailure, terminal); }
            throw;
        }
        await ClusterRestoreRf3NativeArchive.RequireOriginalAsync(originals, archives,
            cancellationToken).ConfigureAwait(false);
        await source.RestartJoinedAsync(cancellationToken).ConfigureAwait(false);
        await seed.VerifyAsync(cancellationToken).ConfigureAwait(false);
        await ClusterRestoreRf3Scenario.RunAsync(source, seed, Path.Combine(ownedRoot, HealthyTarget),
            cancellationToken).ConfigureAwait(false);
    }
}
