using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed record BlobPersistedCorruptionFixture(
    BlobRef Blob,
    Guid UploadId,
    BlobRef HealthyBlob,
    BlobMetadata HealthyMetadata,
    byte[] HealthyBytes,
    bool Published);

internal static class BlobPersistedCorruptionFixtureFactory
{
    private const string StateBlobId = "persisted-state-under-test";
    private const string HeadBlobId = "persisted-head-under-test";
    private const string HealthyBlobId = "persisted-healthy-peer";
    private const string PrincipalId = "root";
    private const int PartOrdinal = 0;
    private const long InitialRevision = 0;
    private static readonly byte[] Payload = [0x31, 0x32, 0x33, 0x34];

    internal static BlobPersistedCorruptionFixture CreateState(TestDatabase database,
        BlobPersistedStateCase testCase)
    {
        BlobStorageTestSupport.Configure(database);
        var blob = BlobStorageTestSupport.Blob(database, StateBlobId);
        var uploadId = Guid.NewGuid();
        BlobStorageTestSupport.Begin(database, blob, uploadId, Payload.Length, InitialRevision);
        var published = testCase == BlobPersistedStateCase.CompletePartCount;
        if (testCase != BlobPersistedStateCase.InitialHashMismatch)
        {
            BlobStorageTestSupport.Write(database, blob, uploadId, PartOrdinal, Payload);
        }
        if (published)
        {
            var chain = Chain(database, blob, uploadId);
            BlobStorageTestSupport.Complete(database, blob, uploadId, chain);
        }

        var healthy = Publish(database, HealthyBlobId);
        return new(blob, uploadId, healthy.Blob, healthy.Metadata, Payload, published);
    }

    internal static BlobPersistedCorruptionFixture CreateHead(TestDatabase database,
        BlobPersistedHeadCase testCase)
    {
        BlobStorageTestSupport.Configure(database);
        var blob = BlobStorageTestSupport.Blob(database, HeadBlobId);
        var uploadId = Guid.NewGuid();
        var published = testCase is not (BlobPersistedHeadCase.UnpublishedLength
            or BlobPersistedHeadCase.UnpublishedPartCount or BlobPersistedHeadCase.UnpublishedHash
            or BlobPersistedHeadCase.UnpublishedLiveRevision);
        BlobStorageTestSupport.Begin(database, blob, uploadId, Payload.Length, InitialRevision);
        if (published)
        {
            BlobStorageTestSupport.Write(database, blob, uploadId, PartOrdinal, Payload);
            BlobStorageTestSupport.Complete(database, blob, uploadId, Chain(database, blob, uploadId));
        }

        var healthy = Publish(database, HealthyBlobId);
        return new(blob, uploadId, healthy.Blob, healthy.Metadata, Payload, published);
    }

    internal static string Chain(TestDatabase database, BlobRef blob, Guid uploadId)
    {
        var hash = BlobIntegrity.InitialHash(database.Store.Identity.Incarnation, blob, uploadId, Payload.Length);
        return BlobIntegrity.NextHash(hash, PartOrdinal, Payload.Length, BlobIntegrity.PartHash(Payload));
    }

    private static (BlobRef Blob, BlobMetadata Metadata) Publish(TestDatabase database, string id)
    {
        var blob = BlobStorageTestSupport.Blob(database, id);
        var uploadId = Guid.NewGuid();
        BlobStorageTestSupport.Begin(database, blob, uploadId, Payload.Length, InitialRevision);
        BlobStorageTestSupport.Write(database, blob, uploadId, PartOrdinal, Payload);
        var metadata = BlobStorageTestSupport.Complete(database, blob, uploadId, Chain(database, blob, uploadId)).Value;
        return (blob, metadata);
    }
}

internal sealed record BlobPersistedSnapshot(
    byte[]? Head,
    byte[]? State,
    byte[]? Quota,
    byte[]? GlobalQuota,
    byte[]? Part,
    byte[]? PartMetadata,
    byte[]? Clock,
    long Position,
    long LastApplied);

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
