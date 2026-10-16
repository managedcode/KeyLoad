using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class NativeDropTableCleanupPages
{
    internal static async Task ExecuteAsync(NativeDropTableContext context, ResourceRetirementStateV1 immutable,
        RegisteredPhysicalOwnerV1 receiver, PartitionRef partition)
    {
        var proof = await NativeDropTableProgress.ReadAsync(context, immutable.Control, receiver, partition, release: false).ConfigureAwait(false);
        var previous = proof.CleanupCheckpoint;
        while (previous?.Complete != true)
        {
            context.Work.Check();
            previous = await NativeDropTablePageSequence.ExecuteAsync(context, immutable, receiver, partition, previous)
                .ConfigureAwait(false);
        }
    }
}
