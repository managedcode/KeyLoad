using KeyLoad.Core.BlobStorage;
using ManagedCode.Communication;

namespace KeyLoad.FunctionalTests.BlobStorage;

internal sealed class FileAuthorizationTests
{
    private const string Resource = "files";
    private const string Path = "private.txt";
    private const string OtherPath = "other.txt";
    private const string Root = "root";
    private const string Reader = "reader";
    private const string Owner = "owner";
    private const string OtherOwner = "other-owner";
    private const string Node = "node";
    private const string Mime = "text/plain";
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const int Length = 4;
    private const int EpochStep = 1;
    private const int NoRecords = 0;

    [Test]
    public async Task RowVisibilityAndFreshRevocationApplyToMetadataAndListing()
    {
        using var database = new TestDatabase();
        database.Configure(Resource, ResourceKind.BlobStore);
        var request = Request(database);
        _ = Apply(database, request).Get<FileUpload>();
        request = request with { CommandId = Guid.NewGuid(), Action = FileMetadataAction.Stage, ObjectId = Guid.NewGuid() };
        _ = Apply(database, request).Get<FileUpload>();
        _ = Apply(database, request with { CommandId = Guid.NewGuid(), Action = FileMetadataAction.Publish, Sha256 = Hash }).Get<FileEntry>();
        var principal = new PrincipalRecord(Reader, database.Partition.TenantId,
            [new(database.Partition.DatabaseId, Resource, Capability.BlobRead)], [])
        {
            RestrictRows = true,
            OwnerId = OtherOwner
        };
        _ = database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
        var list = new FileMetadataQuery(request.Address with { Path = string.Empty }, Capability.BlobRead, Guid.Empty, List: true);
        await Assert.That(database.Database.ReadFileMetadata(Reader, list).Entries.Length).IsEqualTo(NoRecords);
        principal = principal with { PolicyEpoch = principal.PolicyEpoch + EpochStep, OwnerId = Owner };
        _ = database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
        await Assert.That(database.Database.ReadFileMetadata(Reader, list).Entries.Single().ObjectId).IsEqualTo(request.ObjectId);
        _ = database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal with
        {
            PolicyEpoch = principal.PolicyEpoch + EpochStep,
            Revoked = true
        })).Get<PrincipalRecord>();
        await Assert.That(() => database.Database.ReadFileMetadata(Reader, list)).Throws<KeyLoadException>();
    }

    [Test]
    public async Task ReservedBytesRemainAccountedUntilJoinedAbortAndReclaim()
    {
        using var database = new TestDatabase();
        var definition = new ResourceDefinition(Resource, ResourceKind.BlobStore, database.Partition.TransactionDomainId)
        {
            FilePolicy = new() { MaximumFileBytes = Length, MaximumStoredBytes = Length }
        };
        _ = database.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(database.Partition.TenantId, database.Partition.DatabaseId, definition)).Get<ResourceDefinition>();
        var first = Request(database);
        _ = Apply(database, first).Get<FileUpload>();
        var second = Request(database) with { Address = first.Address with { Path = OtherPath } };
        await Assert.That(Errors.Code(Apply(database, second).Problem).Value).IsEqualTo(ErrorCode.ResourceExhausted);
        _ = Apply(database, first with { CommandId = Guid.NewGuid(), Action = FileMetadataAction.Abort }).Get<FileUpload>();
        await Assert.That(Errors.Code(Apply(database, second with { CommandId = Guid.NewGuid() }).Problem).Value).IsEqualTo(ErrorCode.ResourceExhausted);
        _ = Apply(database, first with { CommandId = Guid.NewGuid(), Action = FileMetadataAction.Reclaim }).Get<bool>();
        _ = Apply(database, second with { CommandId = Guid.NewGuid() }).Get<FileUpload>();
    }

    [Test]
    public async Task ResourceManagementGrantCannotReadAHiddenUploadLease()
    {
        using var database = new TestDatabase();
        database.Configure(Resource, ResourceKind.BlobStore);
        var request = Request(database);
        _ = Apply(database, request).Get<FileUpload>();
        var principal = new PrincipalRecord(Reader, database.Partition.TenantId,
            [new(database.Partition.DatabaseId, Resource, Capability.BlobManage)], [])
        {
            RestrictRows = true,
            OwnerId = OtherOwner
        };
        _ = database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
        var query = new FileMetadataQuery(request.Address with { Path = string.Empty }, Capability.BlobManage, request.UploadId);
        await Assert.That(() => database.Database.ReadFileMetadata(Reader, query)).Throws<KeyLoadException>();
        await Assert.That(database.Database.ReadFileMetadata(Root, query).Upload!.UploadId).IsEqualTo(request.UploadId);
    }

    private static FileMetadataMutation Request(TestDatabase database)
        => new(Guid.NewGuid(), new(database.Partition, Resource, Path), FileMetadataAction.Reserve, Guid.NewGuid(), Guid.Empty,
            Length, Mime, string.Empty, new(Owner), Node);

    private static Result<object> Apply(TestDatabase database, FileMetadataMutation request)
        => database.SubmitIssued(database.Database.CreateCommand(OperationKind.FileMetadata, request.CommandId,
            Root, default, request));
}
