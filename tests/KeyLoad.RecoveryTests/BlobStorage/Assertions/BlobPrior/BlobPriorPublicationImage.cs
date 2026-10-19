using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobPriorPublicationImage
{
    internal static string[] Capture(ZoneTreeStore store) => store.Read(view =>
    {
        var page = view.Scan([], 4096);
        if (page.HasMore)
        {
            throw new InvalidOperationException("The prior publication process image exceeded its original row bound.");
        }
        return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)).ToArray();
    });

    internal static async Task ExactAsync<T>(T actual, T expected)
        => await Assert.That(NativeSerialization.Serialize(actual).AsSpan().SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();

    internal static async Task SameAsync(ZoneTreeStore store, string[] image, long position, byte[] identity)
    {
        await Assert.That(Capture(store).SequenceEqual(image)).IsTrue();
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(NativeSerialization.Serialize(store.Identity).AsSpan().SequenceEqual(identity)).IsTrue();
    }

    internal static async Task ReplayAsync(DatabaseEngine owner, BlobPriorPublicationCommand command)
    {
        var store = (ZoneTreeStore)owner.Store;
        var image = Capture(store);
        var position = store.Position;
        var identity = NativeSerialization.Serialize(store.Identity);
        await ExactAsync(owner.Apply(command.Operation), command.Result);
        await ExactAsync(owner.ResolveOutcome(command.Operation), command.Result);
        await SameAsync(store, image, position, identity);
    }

    internal static async Task ReceiptAsync(DatabaseEngine owner, CommitReceipt receipt, BlobPriorPublicationCommand command, BlobRef blob)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(command.Operation.CommandId);
        await ExactAsync(receipt.Token, command.ExpectedToken);
        await Assert.That(owner.Store.Position).IsEqualTo(checked(command.PreviousPosition + 1L));
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(blob.Partition.AtomicPartitionId);
        await Assert.That(receipt.Durability).IsEqualTo(owner.Store.Identity.Durability);
        await Assert.That(receipt.Mutations).IsEmpty();
    }
}
