using System.Text.Json;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class BlobPersistedCorruptionAssertions
{
    private const string PrincipalId = "root";
    private const int ReclaimLimit = 1;
    private const long InitialRevision = 0;
    private const int PayloadLength = 4;
    private const string CorruptionInjectionFailed = "The intentional persisted blob corruption was not stored exactly.";

    internal static void CorruptState(TestDatabase database, BlobPersistedCorruptionFixture fixture,
        BlobPersistedStateCase testCase)
    {
        var key = BlobKeys.State(fixture.Blob, fixture.UploadId);
        var state = ReadRecord<BlobState>(database, key);
        if (BlobPersistedStateMutation.IsQuotaCase(testCase))
        {
            var quotaKey = BlobKeys.Quota(fixture.Blob);
            var quota = ReadRecord<BlobQuota>(database, quotaKey);
            Replace(database, quotaKey, BlobPersistedStateMutation.Apply(quota, testCase));
            return;
        }
        Replace(database, key, BlobPersistedStateMutation.Apply(state, testCase, fixture.Blob,
            fixture.UploadId, database.Store.Identity.Incarnation));
    }

    internal static void CorruptHead(TestDatabase database, BlobPersistedCorruptionFixture fixture,
        BlobPersistedHeadCase testCase)
    {
        var key = BlobKeys.Head(fixture.Blob);
        var head = ReadRecord<BlobHead>(database, key);
        Replace(database, key, BlobPersistedHeadMutation.Apply(head, testCase,
            database.Store.Identity.Incarnation));
    }

    internal static async Task AssertStateRejectedAsync(TestDatabase database,
        BlobPersistedCorruptionFixture fixture, ErrorCode expected)
    {
        var operations = new BlobStorageOperations(database.Database);
        var readFailure = Assert.ThrowsExactly<KeyLoadException>(() => operations.UploadInfo(PrincipalId,
            new(fixture.Blob, fixture.UploadId)));
        await Assert.That(readFailure.Code).IsEqualTo(expected);
        var commandId = Guid.NewGuid();
        var before = BlobPersistedSnapshotReader.Capture(database, fixture.Blob, fixture.UploadId);
        var request = new ReclaimBlobRequest(commandId, fixture.Blob, fixture.UploadId, ReclaimLimit);
        var writeFailure = Assert.ThrowsExactly<KeyLoadException>(() => Apply(database, OperationKind.ReclaimBlob,
            commandId, request));
        await Assert.That(writeFailure.Code).IsEqualTo(expected);
        await AssertUnchangedAsync(database, fixture, before, commandId);
        await AssertHealthyBlobAsync(operations, fixture);
    }

    internal static async Task AssertHeadRejectedAsync(TestDatabase database,
        BlobPersistedCorruptionFixture fixture, ErrorCode expected)
    {
        var operations = new BlobStorageOperations(database.Database);
        var readFailure = Assert.ThrowsExactly<KeyLoadException>(() => operations.Metadata(PrincipalId,
            new(fixture.Blob)));
        await Assert.That(readFailure.Code).IsEqualTo(expected);
        var commandId = Guid.NewGuid();
        var before = BlobPersistedSnapshotReader.Capture(database, fixture.Blob, fixture.UploadId);
        var request = new BeginBlobUploadRequest(commandId, fixture.Blob, Guid.NewGuid(), PayloadLength,
            fixture.Published ? 1 : InitialRevision);
        var writeFailure = Assert.ThrowsExactly<KeyLoadException>(() => Apply(database,
            OperationKind.BeginBlobUpload, commandId, request));
        await Assert.That(writeFailure.Code).IsEqualTo(expected);
        await AssertUnchangedAsync(database, fixture, before, commandId);
        await AssertHealthyBlobAsync(operations, fixture);
    }

    private static OperationResult Apply<T>(TestDatabase database, OperationKind kind, Guid commandId, T request)
        => database.Database.Apply(new(commandId, kind, PrincipalId, TimeProvider.System.GetUtcNow(),
            JsonSerializer.Serialize(request, JsonDefaults.Options)));

    private static async Task AssertUnchangedAsync(TestDatabase database, BlobPersistedCorruptionFixture fixture,
        BlobPersistedSnapshot before, Guid commandId)
    {
        var after = BlobPersistedSnapshotReader.Capture(database, fixture.Blob, fixture.UploadId);
        await Assert.That(BlobPersistedSnapshotReader.SameBytes(after.Head, before.Head)).IsTrue();
        await Assert.That(BlobPersistedSnapshotReader.SameBytes(after.State, before.State)).IsTrue();
        await Assert.That(BlobPersistedSnapshotReader.SameBytes(after.Quota, before.Quota)).IsTrue();
        await Assert.That(BlobPersistedSnapshotReader.SameBytes(after.GlobalQuota, before.GlobalQuota)).IsTrue();
        await Assert.That(BlobPersistedSnapshotReader.SameBytes(after.Part, before.Part)).IsTrue();
        await Assert.That(BlobPersistedSnapshotReader.SameBytes(after.PartMetadata, before.PartMetadata)).IsTrue();
        await Assert.That(BlobPersistedSnapshotReader.SameBytes(after.Clock, before.Clock)).IsTrue();
        await Assert.That(after.Position).IsEqualTo(before.Position);
        await Assert.That(after.LastApplied).IsEqualTo(before.LastApplied);
        await Assert.That(BlobPersistedSnapshotReader.Read(database, OutcomeStoreOracle.PartitionKey(fixture.Blob.Partition, PrincipalId, commandId))).IsNull();
    }

    private static async Task AssertHealthyBlobAsync(BlobStorageOperations operations,
        BlobPersistedCorruptionFixture fixture)
    {
        var healthy = operations.Read(PrincipalId,
            new(fixture.HealthyBlob, fixture.HealthyMetadata.Revision, 0, fixture.HealthyBytes.Length));
        await Assert.That(healthy.Metadata).IsEqualTo(fixture.HealthyMetadata);
        await Assert.That(healthy.Bytes.Span.SequenceEqual(fixture.HealthyBytes)).IsTrue();
        var metadata = operations.Metadata(PrincipalId, new(fixture.HealthyBlob));
        await Assert.That(metadata).IsEqualTo(fixture.HealthyMetadata);
    }

    private static T ReadRecord<T>(TestDatabase database, byte[] key) where T : class
    {
        var bytes = BlobPersistedSnapshotReader.Read(database, key)
            ?? throw new InvalidOperationException("Expected persisted blob record.");
        return NativeSerialization.Deserialize<T>(bytes);
    }

    private static void Replace<T>(TestDatabase database, byte[] key, T record)
    {
        // Invoke the generated native codec directly to seed intentionally malformed persisted state.
        var bytes = NativeSerializerProviders.Get(typeof(T)).Serializer.SerializeToArray(
            new NativePayload { Version = NativePayloadVersion.Current, Value = record });
        database.Store.Commit((transaction, _) =>
        {
            transaction.Put(key, bytes);
            return true;
        });
        if (!BlobPersistedSnapshotReader.SameBytes(BlobPersistedSnapshotReader.Read(database, key), bytes))
        {
            throw new InvalidOperationException(CorruptionInjectionFailed);
        }
    }
}
