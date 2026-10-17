using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class BlobPersistedSnapshotReader
{
    internal static BlobPersistedSnapshot Capture(TestDatabase database, BlobRef blob, Guid uploadId)
        => new(Read(database, BlobKeys.Head(blob)), Read(database, BlobKeys.State(blob, uploadId)),
            Read(database, BlobKeys.Quota(blob)), Read(database, BlobKeys.Global),
            Read(database, BlobKeys.Part(blob, uploadId, 0)), Read(database, BlobKeys.PartMeta(blob, uploadId, 0)),
            Read(database, KeySpace.Clock.ToArray()), database.Store.Position, database.Database.LastApplied);

    internal static byte[]? Read(TestDatabase database, byte[] key)
        => database.Store.Read(view => view.ReadOwnedValue(key));

    internal static bool SameBytes(byte[]? left, byte[]? right)
        => left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);
}
