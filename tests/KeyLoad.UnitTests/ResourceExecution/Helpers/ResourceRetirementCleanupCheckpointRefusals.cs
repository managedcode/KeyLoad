using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementCleanupNativeData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementCleanupCheckpointRefusals
{
    internal static async Task RunAsync(TestDatabase fixture)
    {
        var state = ResourceRetirementCleanupNativeSeed.Create(fixture);
        var checkpoint = ResourceRetirementCleanupCheckpointShape.Create(fixture, state);
        var limits = fixture.Database.Limits;
        // Successful shape validation is not an authenticated retirement or cleanup acknowledgement.
        ResourceRetirementCleanupCheckpointValidation.Require(checkpoint, limits);
        var image = ResourceRetirementCleanupNativeImages.Read(fixture.Store);
        var position = fixture.Store.Position;
        var otherPartition = checkpoint with
        {
            Partition = state.Partitions[SecondIndex]
        };
        var otherOrdinal = checkpoint with
        {
            Ordinal = checked(checkpoint.Ordinal + One)
        };
        await InvalidAsync(fixture, otherPartition, image, position);
        await InvalidAsync(fixture, otherOrdinal, image, position);
        await Assert.That(ResourceRetirementCleanupPageIdentity.For(checkpoint.Control, checkpoint.Receiver,
            otherPartition.Partition, checkpoint.Ordinal, checkpoint.AttemptId)).IsNotEqualTo(checkpoint.Receipt.CommandId);
        await Assert.That(ResourceRetirementCleanupPageIdentity.For(checkpoint.Control, checkpoint.Receiver,
            checkpoint.Partition, otherOrdinal.Ordinal, checkpoint.AttemptId)).IsNotEqualTo(checkpoint.Receipt.CommandId);
        await StoredAsync(fixture, checkpoint);
    }

    private static async Task InvalidAsync(TestDatabase fixture, ResourceRetirementCleanupCheckpointV1 invalid,
        Dictionary<string, string> image, long position)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
            ResourceRetirementCleanupCheckpointValidation.Require(invalid, fixture.Database.Limits));
        await Assert.That(error!.Code).IsEqualTo(ErrorCode.Corruption);
        await ResourceRetirementCleanupNativeImages.SameAsync(fixture.Store, image, position);
    }

    private static async Task StoredAsync(TestDatabase fixture, ResourceRetirementCleanupCheckpointV1 checkpoint)
    {
        var key = ResourceRetirementCleanupStorage.Key(checkpoint.Control, checkpoint.Receiver, checkpoint.Partition);
        var original = NativeSerialization.Serialize(checkpoint);
        var wrongId = ResourceRetirementCleanupPageIdentity.For(checkpoint.Control, checkpoint.Receiver,
            checkpoint.Partition, checked(checkpoint.Ordinal + One), checkpoint.AttemptId);
        var corrupt = checkpoint with
        {
            Receipt = checkpoint.Receipt with
            {
                CommandId = wrongId
            }
        };
        fixture.Store.Commit((transaction, _) => { transaction.PutRecord(key, corrupt); return true; });
        var image = ResourceRetirementCleanupNativeImages.Read(fixture.Store);
        var position = fixture.Store.Position;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Store.Read(view =>
            ResourceRetirementCleanupStorage.Read(view, checkpoint.Control, checkpoint.Receiver,
                checkpoint.Partition, fixture.Database.Limits)));
        await Assert.That(error!.Code).IsEqualTo(ErrorCode.Corruption);
        await ResourceRetirementCleanupNativeImages.SameAsync(fixture.Store, image, position);
        fixture.Store.Commit((transaction, _) => { transaction.Put(key, original); return true; });
        var repaired = fixture.Store.Read(view => ResourceRetirementCleanupStorage.Read(view, checkpoint.Control,
            checkpoint.Receiver, checkpoint.Partition, fixture.Database.Limits));
        await Assert.That(repaired).IsNotNull();
        await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(repaired!)))
            .IsEqualTo(Convert.ToHexString(original));
    }
}
