using KeyLoad.Clustering;
using KeyLoad.Core.BlobStorage;
using KeyLoad.Core.RuntimeJournaling.Identity;
using KeyLoad.FunctionalTests.Authorization;
using KeyLoad.FunctionalTests.OrleansExecution;

namespace KeyLoad.FunctionalTests.InternalSerialization;

internal sealed class OperationCoordinatorTests
{
    private const string RootPrincipal = "root";
    private const string Collection = "execution-operations";
    private const string Entity = "execution-entry";
    private const string Json = "{\"name\":\"界λ\"}";
    private const string Files = "typed-command-files";
    private const string FilePath = "reports/typed.bin";
    private const string FileHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly DateTimeOffset Time = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task CommandPersistsTypedOutcomeAndReplaysTheSameStableId()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, database.Partition, [new PutDocument(Collection, Entity, Json)]);
        var coordinator = new EmbeddedCoordinator(database.Database);
        var payload = request;
        var first = await coordinator.RuntimeSubmitAsync<CommitReceipt>(OperationKind.Batch, id, RootPrincipal, payload);
        var replay = await coordinator.RuntimeSubmitAsync<CommitReceipt>(OperationKind.Batch, id, RootPrincipal, payload);
        await Assert.That(first.Value).IsTypeOf<CommitReceipt>();
        await Assert.That(replay.Get<CommitReceipt>().Token).IsEqualTo(first.Get<CommitReceipt>().Token);
        await Assert.That(replay.Get<CommitReceipt>().CommandId).IsEqualTo(id);
        await Assert.That(database.Database.GetDocument(RootPrincipal, new(database.Partition, Collection, Entity))!.Revision)
            .IsEqualTo(1L);
    }

    [Test]
    public async Task WrongCommandTypeFailsBeforeAnyStoreMutation()
    {
        using var database = new TestDatabase();
        var before = database.Store.Position;
        var coordinator = new EmbeddedCoordinator(database.Database);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => coordinator.RuntimeSubmitAsync<CommitReceipt>(OperationKind.Batch,
            Guid.NewGuid(), RootPrincipal, true));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Store.Position).IsEqualTo(before);
    }

    [Test]
    public async Task CommandRetainsExactInputAndOpaqueUserJson()
    {
        using var database = new TestDatabase();
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, database.Partition, [new PutDocument(Collection, Entity, Json)]);
        var operation = database.Database.CreateCommand(OperationKind.Batch, id, RootPrincipal, Time,
            request);
        await Assert.That(operation.CommandId).IsEqualTo(id);
        await Assert.That(operation.UserId).IsEqualTo(RootPrincipal);
        await Assert.That(operation.Timestamp).IsEqualTo(Time.UtcDateTime);
        await Assert.That(OrleansSerialization.Serialize(operation.Value!.Input).AsSpan().SequenceEqual(OrleansSerialization.Serialize(request))).IsTrue();
        var restored = ((CommandRequest)operation.Value.Input);
        await Assert.That(((PutDocument)restored.Mutations[0]).Json).IsEqualTo(Json);
    }

    [Test]
    public async Task SignedJournalMutationReturnsConcreteReplyAndReplaysStableIdentity()
    {
        using var database = new TestDatabase();
        JournalAuthorityTestBootstrap.Initialize(database.Database);
        var mutation = JournalAuthorityTestBootstrap.CreateMutation();
        var commandId = Guid.NewGuid();
        var before = database.Store.Position;
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => DispatchAsync(database,
            OperationKind.RuntimeJournal, commandId, mutation, RuntimeJournalIdentity.ProtectedPrincipalId, cancelled.Token));
        await Assert.That(database.Store.Position).IsEqualTo(before);
        var created = Reply<RuntimeJournalMutationResult>(await DispatchAsync(database,
            OperationKind.RuntimeJournal, commandId, mutation, RuntimeJournalIdentity.ProtectedPrincipalId));
        await Assert.That(created.Applied).IsTrue();
        await Assert.That(created.Snapshot!.InstanceId).IsEqualTo(mutation.InstanceId);
        var stored = OrleansSerialization.Serialize(database.ReadStoredOutcome(commandId));
        var replay = Reply<RuntimeJournalMutationResult>(await DispatchAsync(database,
            OperationKind.RuntimeJournal, commandId, mutation, RuntimeJournalIdentity.ProtectedPrincipalId));
        await Assert.That(replay.Snapshot!.MetadataETag).IsEqualTo(created.Snapshot.MetadataETag);
        await Assert.That(OrleansSerialization.Serialize(database.ReadStoredOutcome(commandId)).SequenceEqual(stored)).IsTrue();
        var append = RuntimeJournalFixture.Mutation(RuntimeJournalAction.Append, created.Snapshot, [0, 1, 255]);
        var appended = Reply<RuntimeJournalMutationResult>(await DispatchAsync(database,
            OperationKind.RuntimeJournal, Guid.NewGuid(), append, RuntimeJournalIdentity.ProtectedPrincipalId));
        await Assert.That(appended.Snapshot!.Length).IsEqualTo(3L);
    }

    [Test]
    public async Task SignedFilePublicationReturnsUploadEntryAndBooleanReplies()
    {
        using var database = new TestDatabase();
        database.Configure(Files, ResourceKind.BlobStore);
        var reserve = FileRequest(database);
        var upload = Reply<FileUpload>(await DispatchAsync(database, OperationKind.FileMetadata, reserve.CommandId, reserve));
        await Assert.That(upload.UploadId).IsEqualTo(reserve.UploadId);
        var stored = OrleansSerialization.Serialize(database.ReadStoredOutcome(reserve.CommandId));
        var replay = Reply<FileUpload>(await DispatchAsync(database, OperationKind.FileMetadata, reserve.CommandId, reserve));
        await Assert.That(replay.UploadId).IsEqualTo(upload.UploadId);
        await Assert.That(OrleansSerialization.Serialize(database.ReadStoredOutcome(reserve.CommandId)).SequenceEqual(stored)).IsTrue();
        var stage = reserve with { CommandId = Guid.NewGuid(), Action = FileMetadataAction.Stage, ObjectId = Guid.NewGuid() };
        var staged = Reply<FileUpload>(await DispatchAsync(database, OperationKind.FileMetadata, stage.CommandId, stage));
        await Assert.That(staged.ObjectId).IsEqualTo(stage.ObjectId);
        var publish = stage with { CommandId = Guid.NewGuid(), Action = FileMetadataAction.Publish, Sha256 = FileHash };
        var published = Reply<FileEntry>(await DispatchAsync(database, OperationKind.FileMetadata, publish.CommandId, publish));
        await Assert.That(published.ObjectId).IsEqualTo(stage.ObjectId);
        var reclaim = stage with { CommandId = Guid.NewGuid(), Action = FileMetadataAction.Reclaim };
        var denied = await DispatchAsync(database, OperationKind.FileMetadata, reclaim.CommandId, reclaim);
        await Assert.That(denied.Error).IsEqualTo(ErrorCode.Conflict);
        var delete = stage with { CommandId = Guid.NewGuid(), Action = FileMetadataAction.Delete };
        var deleted = Reply<FileEntry>(await DispatchAsync(database, OperationKind.FileMetadata, delete.CommandId, delete));
        await Assert.That(deleted.Deleted).IsTrue();
        reclaim = reclaim with { CommandId = Guid.NewGuid() };
        await Assert.That(Reply<bool>(await DispatchAsync(database, OperationKind.FileMetadata, reclaim.CommandId, reclaim))).IsTrue();
        var state = database.Database.ReadFileMetadata(RootPrincipal, new(reserve.Address, Capability.BlobRead, reserve.UploadId));
        await Assert.That(state.Upload).IsNull();
        await Assert.That(state.Entry!.Deleted).IsTrue();
        var next = FileRequest(database);
        await Assert.That(Reply<FileUpload>(await DispatchAsync(database, OperationKind.FileMetadata, next.CommandId, next)).UploadId)
            .IsEqualTo(next.UploadId);
    }

    [Test]
    public async Task SignedFileAbortCanBeReclaimedThroughItsBooleanReply()
    {
        using var database = new TestDatabase();
        database.Configure(Files, ResourceKind.BlobStore);
        var reserve = FileRequest(database);
        _ = Reply<FileUpload>(await DispatchAsync(database, OperationKind.FileMetadata, reserve.CommandId, reserve));
        var abort = reserve with { CommandId = Guid.NewGuid(), Action = FileMetadataAction.Abort };
        var aborted = Reply<FileUpload>(await DispatchAsync(database, OperationKind.FileMetadata, abort.CommandId, abort));
        await Assert.That(aborted.Aborted).IsTrue();
        var reclaim = abort with { CommandId = Guid.NewGuid(), Action = FileMetadataAction.Reclaim };
        await Assert.That(Reply<bool>(await DispatchAsync(database, OperationKind.FileMetadata, reclaim.CommandId, reclaim))).IsTrue();
        await Assert.That(database.Database.ReadFileMetadata(RootPrincipal,
            new(reserve.Address, Capability.BlobRead, reserve.UploadId)).Upload).IsNull();
    }

    private static FileMetadataMutation FileRequest(TestDatabase database)
        => new(Guid.NewGuid(), new(database.Partition, Files, FilePath), FileMetadataAction.Reserve, Guid.NewGuid(), Guid.Empty,
            4, "application/octet-stream", string.Empty, new(RootPrincipal), "typed-command-owner");

    private static T Reply<T>(GrainOperationReply reply) => (T)GrainReplyFactory.RequireValue(reply).Value!;

    private static async Task<GrainOperationReply> DispatchAsync<T>(TestDatabase database, OperationKind kind,
        Guid commandId, T payload, string principal = RootPrincipal, CancellationToken token = default) where T : notnull
    {
        var codec = new GrainRequestCodec(database.Store.Identity.Incarnation, TimeProvider.System);
        var envelope = kind == OperationKind.RuntimeJournal
            ? codec.CreateRuntimeJournalCommand(Guid.NewGuid(), principal, commandId, payload)
            : codec.CreateCommand(Guid.NewGuid(), principal, kind, commandId, payload);
        var request = codec.Decode(envelope);
        var executor = new GrainCommandExecutor(database.Database, new EmbeddedCoordinator(database.Database), TimeProvider.System);
        return await SignedGrainRequestTestContext.ExecuteAsync(executor, request, GrainPartitionResolver.Resolve(request), token);
    }
}
