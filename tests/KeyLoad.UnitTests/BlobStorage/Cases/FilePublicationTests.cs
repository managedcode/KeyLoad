using KeyLoad.Core.BlobStorage;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.BlobStorage;

internal sealed class FilePublicationTests
{
    private const string UnsafePath = "../outside";
    private const string Resource = "files";
    private const string Path = "reports/annual.pdf";
    private const string Root = "root";
    private const string Node = "node-a";
    private const string OtherNode = "node-b";
    private const string ContentType = "application/pdf";
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const int Length = 4;

    [Test]
    public async Task PublicationRequiresAStagedObjectAndCleanupCannotRemoveVisibleContent()
    {
        using var database = new TestDatabase();
        database.Configure(Resource, ResourceKind.BlobStore);
        var reserved = Request(database, FileMetadataAction.Reserve);
        _ = Apply(database, reserved).Get<FileUpload>();
        var objectId = Guid.NewGuid();
        var publish = reserved with { CommandId = Guid.NewGuid(), Action = FileMetadataAction.Publish, ObjectId = objectId, Sha256 = Hash };
        await Assert.That(Errors.Code(Apply(database, publish).Problem)).IsEqualTo(ErrorCode.Validation);
        var stage = publish with { CommandId = Guid.NewGuid(), Action = FileMetadataAction.Stage };
        _ = Apply(database, stage).Get<FileUpload>();
        var entry = Apply(database, publish with { CommandId = Guid.NewGuid() }).Get<FileEntry>();
        await Assert.That(Read(database, reserved).Entry).IsEqualTo(entry);
        var reclaim = stage with { CommandId = Guid.NewGuid(), Action = FileMetadataAction.Reclaim };
        await Assert.That(Errors.Code(Apply(database, reclaim).Problem)).IsEqualTo(ErrorCode.Conflict);
        _ = Apply(database, stage with { CommandId = Guid.NewGuid(), Action = FileMetadataAction.Delete }).Get<FileEntry>();
        await Assert.That(Apply(database, reclaim with { CommandId = Guid.NewGuid() }).Get<bool>()).IsTrue();
        await Assert.That(Read(database, reserved).Entry!.Deleted).IsTrue();
    }

    [Test]
    public async Task ConcurrentReservationsCannotOverwriteANewerPublishedRevision()
    {
        using var database = new TestDatabase();
        database.Configure(Resource, ResourceKind.BlobStore);
        var first = Request(database, FileMetadataAction.Reserve);
        var second = Request(database, FileMetadataAction.Reserve);
        _ = Apply(database, first).Get<FileUpload>();
        _ = Apply(database, second).Get<FileUpload>();
        first = first with { CommandId = Guid.NewGuid(), Action = FileMetadataAction.Stage, ObjectId = Guid.NewGuid() };
        second = second with { CommandId = Guid.NewGuid(), Action = FileMetadataAction.Stage, ObjectId = Guid.NewGuid() };
        _ = Apply(database, first).Get<FileUpload>();
        _ = Apply(database, second).Get<FileUpload>();
        var entry = Apply(database, first with { CommandId = Guid.NewGuid(), Action = FileMetadataAction.Publish, Sha256 = Hash }).Get<FileEntry>();
        var rejected = Apply(database, second with { CommandId = Guid.NewGuid(), Action = FileMetadataAction.Publish, Sha256 = Hash });
        await Assert.That(Errors.Code(rejected.Problem)).IsEqualTo(ErrorCode.RevisionConflict);
        await Assert.That(Read(database, first).Entry).IsEqualTo(entry);
    }

    [Test]
    public async Task TransferOwnerFenceAndAbortProtectAnInFlightPhysicalWrite()
    {
        using var database = new TestDatabase();
        database.Configure(Resource, ResourceKind.BlobStore);
        var request = Request(database, FileMetadataAction.Reserve);
        _ = Apply(database, request).Get<FileUpload>();
        var stage = request with { CommandId = Guid.NewGuid(), Action = FileMetadataAction.Stage, ObjectId = Guid.NewGuid() };
        await Assert.That(Errors.Code(Apply(database, stage with { TransferOwner = OtherNode }).Problem)).IsEqualTo(ErrorCode.TokenInvalidated);
        _ = Apply(database, stage with { CommandId = Guid.NewGuid() }).Get<FileUpload>();
        var abort = stage with { CommandId = Guid.NewGuid(), Action = FileMetadataAction.Abort, ObjectId = Guid.Empty };
        await Assert.That(Errors.Code(Apply(database, abort).Problem)).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(Read(database, request).Upload!.Aborted).IsFalse();
        _ = Apply(database, abort with { CommandId = Guid.NewGuid(), ObjectId = stage.ObjectId }).Get<FileUpload>();
        await Assert.That(Read(database, request).Upload!.Aborted).IsTrue();
    }

    [Test]
    public async Task MissingPrincipalAndUnsafeLogicalPathCannotCreateMetadata()
    {
        using var database = new TestDatabase();
        database.Configure(Resource, ResourceKind.BlobStore);
        var request = Request(database, FileMetadataAction.Reserve);
        await Assert.That(Apply(database, request, OtherNode).IsFailed).IsTrue();
        var unsafePath = request with { CommandId = Guid.NewGuid(), Address = request.Address with { Path = UnsafePath } };
        await Assert.That(Errors.Code(Apply(database, unsafePath).Problem)).IsEqualTo(ErrorCode.Validation);
        await Assert.That(Read(database, request).Upload).IsNull();
        _ = Apply(database, request with { CommandId = Guid.NewGuid() }).Get<FileUpload>();
        await Assert.That(Read(database, request).Upload).IsNotNull();
    }

    private static FileMetadataMutation Request(TestDatabase database, FileMetadataAction action)
        => new(Guid.NewGuid(), new(database.Partition, Resource, Path), action, Guid.NewGuid(), Guid.Empty,
            Length, ContentType, string.Empty, new(Root), Node);

    private static Result<object> Apply(TestDatabase database, FileMetadataMutation request, string principal = Root)
        => database.SubmitIssued(database.Database.CreateCommand(OperationKind.FileMetadata, request.CommandId,
            principal, default, OrleansSerialization.Serialize(request)));

    private static FileMetadataState Read(TestDatabase database, FileMetadataMutation request)
        => database.Database.ReadFileMetadata(Root, new(request.Address, Capability.BlobRead, request.UploadId));
}
