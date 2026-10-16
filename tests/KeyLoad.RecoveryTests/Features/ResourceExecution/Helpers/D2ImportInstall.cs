using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.CrashHost.Features.ResourceExecution;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal static class D2ImportInstall
{
    internal static async Task StageAsync(D2ImportSession session, D2ImportImage image, CancellationToken token)
    {
        foreach (var actual in image.Pages)
        {
            var page = NativeSerialization.Deserialize<PartitionMoveImagePage>(actual.NativePage.Span);
            await Assert.That(page.Ordinal).IsEqualTo(actual.Ordinal);
            var phase = D2ImportPhases.Phase(session, image.Captured, PartitionMovePeerStage.StagePage,
                NativeSerialization.Serialize(new PartitionMovePageBody(D2ImportCrashProtocol.Principal,
                    image.Captured.Control!, image.Fence, image.Handle.Descriptor, page)), actual.Ordinal);
            _ = await D2ImportPhases.EffectAsync(session, image.Captured, phase, atSource: false, token);
        }
    }

    internal static async Task OneAsync(D2ImportSession session, D2ImportImage image, CancellationToken token)
    {
        var ordinal = image.NextOrdinal;
        var phase = D2ImportPhases.Phase(session, image.Captured, PartitionMovePeerStage.Install,
            NativeSerialization.Serialize(new PartitionMoveInstallBody(D2ImportCrashProtocol.Principal,
                image.Captured.Control!, image.Fence, image.Handle.Descriptor)), ordinal);
        var index = checked(session.Target.Journal.Log.State.LastIndex + D2ImportCrashProtocol.Single);
        var actual = await D2ImportPhases.EffectAsync(session, image.Captured, phase, atSource: false, token);
        await Assert.That(actual.Result.Stage).IsEqualTo(PartitionMovePeerStage.Install);
        if (ordinal < image.Handle.PageCount)
        {
            await Assert.That(actual.Result.InstalledReceipt).IsNull();
        }
        else
        {
            var receipt = new CommitReceipt(actual.Original.Id,
                new(session.Owners.Destination.Owner.Incarnation,
                    image.Captured.Control!.Partition.AtomicPartitionId, index,
                    session.Owners.Destination.Owner.PlacementEpoch), [], DurabilityProfile.ProcessDurable);
            await D2InventoryRecoveryPages.SameAsync(receipt, actual.Result.InstalledReceipt);
        }
        image.Installs.Add((actual.Original, actual.Outcome));
        image.NextOrdinal++;
    }

    internal static async Task CompleteAsync(D2ImportSession session, D2ImportImage image, CancellationToken token)
    {
        while (image.NextOrdinal <= image.Handle.PageCount)
        {
            await OneAsync(session, image, token);
        }
    }
}
