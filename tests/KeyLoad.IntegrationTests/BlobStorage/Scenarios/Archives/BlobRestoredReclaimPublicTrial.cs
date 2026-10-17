using KeyLoad.IntegrationTests.Features.BackupRestore;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.RelationalStorage;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobRestoredReclaimPublicTrial
{
    internal static async Task RunAsync(TwoRf3MembershipWave source, PartitionMovementPublicParentRf3Seed seed,
        string root, CancellationToken token)
    {
        var started = TimeProvider.System.GetTimestamp();
        var state = await BlobRestoredReclaimPublicSeed.CreateAsync(source, seed, token);
        var secondary = await ClusterRestoreRf3SecondaryPartition.SeedAsync(source, seed, token);
        var eventing = await ClusterRestoreRf3EventingSeed.CreateAsync(source, seed, token);
        var originals = await ClusterRestoreRf3Capture.CaptureAsync(source, seed.Partition, Guid.NewGuid(), token);
        var after = seed.Models.Command(new PutDocument(RelationalSqlRf3Tokens.Documents, "after-backup", "{\"afterBackup\":true}"));
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.CommitAsync(after, token));
        await ClusterRestoreRf3RpoOracle.RequireAsync(seed, originals, after, receipt, "after-backup", token);
        var archives = await ClusterRestoreRf3NativeArchive.CopyAsync(source, originals, Path.Combine(root, "blob-off-node"), token);
        await ClusterRestoreRf3ColdCapture.RequireAsync(source, seed, originals, archives, token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var target = new ClusterRestoreRf3Fixture(root, source.Profile.AdminKey, originals, archives);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await target.StartRejectedManifestAsync(token);
                await ClusterRestoreRf3NativeArchive.RequireOriginalAsync(originals, archives, token);
                await ClusterRestoreRf3ArchiveOmissionTrial.RequireAsync(target, originals, archives, root, token);
                var repaired = await ClusterRestoreRf3ArchiveMutationTrial.RequireAndRepairAsync(target, originals, archives, root, token);
                await ClusterRestoreRf3Verification.RunAsync(target, seed, originals, eventing, secondary,
                    null, repaired, null, source.Profile.AdminKey, token);
                await target.StartAsync(restore: false, token);
                await BlobRestoredReclaimPublicFlow.RunAsync(target, seed, state, originals, token);
                await ClusterRestoreRf3NativeArchive.RequireOriginalAsync(originals, archives, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(TimeProvider.System.GetElapsedTime(started)).IsLessThan(ClusterRestoreRf3Protocol.ParentDeadline);
    }
}
