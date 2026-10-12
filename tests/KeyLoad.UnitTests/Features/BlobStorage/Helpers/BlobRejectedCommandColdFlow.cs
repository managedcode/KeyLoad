using System.Collections.Immutable;
using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class BlobRejectedCommandColdFlow
{
    private const string Principal = "root";
    private const string Absent = "<absent>";
    private const int FirstOrdinal = 0;
    private const int Empty = 0;
    private const int Single = 1;

    internal sealed record Retained(Guid Id, OperationKind Kind, string PayloadJson, OperationResult Original);

    private sealed record Scope(BlobRef Blob, Guid Upload, BlobCommitResult<BlobUploadInfo> Begun,
        BlobCommitResult<BlobUploadInfo> Written, BlobMetadata Metadata, ReadOnlyMemory<byte> Bytes,
        ImmutableArray<Retained> Commands);

    internal static Retained Retain<T>(Guid id, OperationKind kind, T request, OperationResult original)
        => new(id, kind, global::System.Text.Json.JsonSerializer.Serialize(request, JsonDefaults.Options), original);

    internal static string[] DomainImage(global::KeyLoad.Core.DatabaseEngine database, BlobRef blob, Guid upload)
    {
        byte[][] keys = [BlobKeys.Head(blob), BlobKeys.State(blob, upload), BlobKeys.Part(blob, upload, FirstOrdinal),
            BlobKeys.PartMeta(blob, upload, FirstOrdinal), BlobKeys.Quota(blob), BlobKeys.Global];
        return database.Store.Read(view => keys.Select(key =>
            view.ReadOwnedValue(key) is { } bytes ? Convert.ToHexString(bytes) : Absent).ToArray());
    }

    internal static async Task DomainUnchanged(global::KeyLoad.Core.DatabaseEngine database,
        BlobRef blob, Guid upload, string[] original)
        => await Assert.That(DomainImage(database, blob, upload)).IsEquivalentTo(original, CollectionOrdering.Matching);

    internal static async Task Run(TestDatabase database, BlobRef blob, Guid upload,
        BlobCommitResult<BlobUploadInfo> begun, BlobCommitResult<BlobUploadInfo> written,
        BlobMetadata metadata, ReadOnlyMemory<byte> bytes, ImmutableArray<Retained> commands)
    {
        var scope = new Scope(blob, upload, begun, written, metadata, bytes, commands);
        await Verify(database.Database, database.Store, scope);
        var position = database.Store.Position;
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        database.Store.Dispose();
        using (var cold = new global::KeyLoad.Storage.ZoneTree.ZoneTreeStore(new(database.Directory),
            database.StorageExecution, UnitExecutionOptions.PointCacheExecution()))
        {
            await BlobWholeFlowAssertions.Unchanged(cold, position, image);
            await Verify(database.CreateConfiguredDatabase(cold), cold, scope);
        }
        using var second = new global::KeyLoad.Storage.ZoneTree.ZoneTreeStore(new(database.Directory),
            database.StorageExecution, UnitExecutionOptions.PointCacheExecution());
        await BlobWholeFlowAssertions.Unchanged(second, position, image);
        await Verify(database.CreateConfiguredDatabase(second), second, scope);
    }

    private static async Task Verify(global::KeyLoad.Core.DatabaseEngine database,
        global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store, Scope scope)
    {
        var position = store.Position;
        var image = QueueWholeFlowStorage.Bytes(store);
        await Models(database, scope);
        var operations = new BlobStorageOperations(database);
        var read = operations.Read(Principal, new(scope.Blob, Single, FirstOrdinal, scope.Bytes.Length));
        await BlobWholeFlowAssertions.Exact(read, new BlobReadResult(scope.Metadata, FirstOrdinal, scope.Bytes));
        foreach (var command in scope.Commands)
        {
            var replay = database.ApplyEmbedded(new(command.Id, command.Kind, Principal, default, command.PayloadJson),
                cancellationToken: default);
            await BlobWholeFlowAssertions.Exact(replay, command.Original);
            await BlobWholeFlowAssertions.Unchanged(store, position, image);
        }
        await Models(database, scope);
        await BlobWholeFlowAssertions.Unchanged(store, position, image);
    }

    private static async Task Models(global::KeyLoad.Core.DatabaseEngine database, Scope scope)
    {
        var incarnation = database.Store.Identity.Incarnation;
        var state = new BlobState(BlobKeys.FormatVersion, incarnation, incarnation, scope.Begun.Receipt.CommandId,
            Principal, scope.Blob, scope.Upload, new RowAccess(), scope.Bytes.Length, Empty,
            scope.Begun.Value.ExpiresAt, Single, scope.Bytes.Length, scope.Metadata.IntegrityHash!,
            BlobUploadStatus.Complete, Empty, Empty);
        await BlobWholeFlowAssertions.Exact(database.Store.Read(view =>
            BlobRecordReader.Get<BlobHead>(view, BlobKeys.Head(scope.Blob))),
            new BlobHead(BlobKeys.FormatVersion, incarnation, scope.Metadata));
        await BlobWholeFlowAssertions.Exact(database.Store.Read(view =>
            BlobRecordReader.Get<BlobState>(view, BlobKeys.State(scope.Blob, scope.Upload))), state);
        await BlobWholeFlowAssertions.Exact(database.Store.Read(view =>
            BlobRecordReader.Get<BlobPartMetadata>(view, BlobKeys.PartMeta(scope.Blob, scope.Upload, FirstOrdinal))),
            new BlobPartMetadata(BlobKeys.FormatVersion, scope.Bytes.Length, BlobIntegrity.PartHash(scope.Bytes.Span)));
        var raw = database.Store.Read(view => view.ReadOwnedValue(BlobKeys.Part(scope.Blob, scope.Upload, FirstOrdinal)));
        await Assert.That(raw).IsNotNull();
        await Assert.That(raw!.AsSpan().SequenceEqual(scope.Bytes.Span)).IsTrue();
        await BlobWholeFlowAssertions.Quotas(database, scope.Blob, scope.Bytes.Length, Single, Empty);
        var operations = new BlobStorageOperations(database);
        await BlobWholeFlowAssertions.Exact(operations.Metadata(Principal, new(scope.Blob)), scope.Metadata);
        await BlobWholeFlowAssertions.Exact(operations.UploadInfo(Principal, new(scope.Blob, scope.Upload)), state.Info);
        await Assert.That(scope.Metadata.VersionId).IsEqualTo(scope.Upload);
        await Assert.That(scope.Metadata.Length).IsEqualTo((long)scope.Bytes.Length);
        await Assert.That(scope.Metadata.PartCount).IsEqualTo(Single);
        await Assert.That(scope.Metadata.Revision).IsEqualTo((long)Single);
        await Assert.That(scope.Written.Value.NextOrdinal).IsEqualTo(Single);
        await Assert.That(scope.Written.Value.StoredBytes).IsEqualTo((long)scope.Bytes.Length);
    }
}
