using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class QueueLeaseAuthorityProcessImage
{
    internal static string[] Capture(ZoneTreeStore store) => store.Read(view =>
    {
        var page = view.Scan([], 4096);
        if (page.HasMore)
        {
            throw new InvalidOperationException("The lease authority process image exceeded its original row bound.");
        }
        return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)).ToArray();
    });

    internal static async Task SameAsync(ZoneTreeStore store, string[] image, long position, byte[] identity)
    {
        await Assert.That(Capture(store).SequenceEqual(image)).IsTrue();
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(OrleansSerialization.Serialize(store.Identity).AsSpan().SequenceEqual(identity)).IsTrue();
    }

    internal static async Task EmptyIndexesAsync(DatabaseEngine database)
    {
        foreach (var family in new[] { "ready", "lease", "scheduled", "dead-letter", "queue-pending-dead-letter",
            "queue-dead-letter-order", "queue-order" })
        {
            var page = database.Store.Read(view => view.Scan(KeySpace.Partition(family,
                QueueOrderedRetryCrashProtocol.Partition, QueueOrderedRetryCrashProtocol.Queue), 1));
            await Assert.That(page.Records).IsEmpty();
            await Assert.That(page.HasMore).IsFalse();
        }
    }

    internal static async Task RefuseAsync(DatabaseEngine database, ZoneTreeStore store, Command<DatabaseCommand> original,
        string[] image, long position, byte[] identity)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(() => database.Apply(original));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(error.Message).IsEqualTo("The queue lifecycle reference does not match its metadata.");
        await Assert.That(OutcomeStoreOracle.Read(store, original)).IsNull();
        await SameAsync(store, image, position, identity);
    }
}
