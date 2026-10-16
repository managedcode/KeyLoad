using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server;
using KeyLoad.Server.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class NativeDropPreparedPageExecution
{
    internal static async Task<ResourceRetirementCleanupCheckpointV1> ExecuteAsync(NativeDropRuntimeProfile profile,
        ResourceRetirementStateV1 original, DateTimeOffset expiry, CancellationToken token)
    {
        var source = profile.Source;
        var work = NativeDropPreparedPageProducer.Work(profile, token);
        work.ConstrainLifetime(expiry);
        var metadata = profile.ReserveMetadata(work);
        var principal = source.Store.Read(raw => source.Database.Principal(work.CreateView(raw, metadata),
            PartitionStoreProtocol.AdministratorId, source.Clock.GetUtcNow()));
        var context = new NativeDropTableContext(source.Partition, profile.Client, source.Runtime, source.Clock,
            profile.Hosts.Control, principal.Id, principal.PolicyEpoch, expiry, work, metadata, token);
        var entry = original.FinalInventory!.Entries.Single(item => item.Partition == NativeDropRuntimeProtocol.Partition);
        var receiver = original.Control.Directory.Owners.Single(owner => owner.Owner.PhysicalShardId == entry.ObservedOwner.PhysicalShardId);
        var checkpoint = await NativeDropTablePageSequence.ExecuteAsync(context, original, receiver,
            entry.Partition, previous: null).ConfigureAwait(false);
        ResourceRetirementCleanupCheckpointValidation.Require(checkpoint, source.Database.Limits);
        await Assert.That(checkpoint.AttemptId == Guid.Empty).IsFalse();
        await Assert.That(checkpoint.Ordinal).IsEqualTo(NativeDropRuntimeProtocol.FirstPage);
        var recovered = await NativeDropTableProgress.ReadAsync(context, original.Control, receiver,
            entry.Partition, release: false).ConfigureAwait(false);
        await NativeDropRuntimeAssertions.SameAsync(checkpoint, recovered.CleanupCheckpoint).ConfigureAwait(false);
        return checkpoint;
    }
}
