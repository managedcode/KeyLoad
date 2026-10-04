using System.Text;
using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed class BlobRowAccessValidationTests
{
    private const string ResourceName = "blobs";
    private const string OwnerBlobId = "invalid-owner-metadata";
    private const string ProjectBlobId = "invalid-project-metadata";
    private const string OwnerControlBlobId = "control-owner-metadata";
    private const string ProjectControlBlobId = "control-project-metadata";
    private const string MaximumBlobId = "maximum-row-metadata";
    private const string HeadSpace = "blob-head-v1";
    private const string StateSpace = "blob-state-v1";
    private const string QuotaSpace = "blob-quota-v1";
    private const string GlobalSpace = "blob-global-v1";
    private const string GuidFormat = "N";
    private const int MaximumIdentifierBytes = 256;
    private const int MaximumAccessRecordBytes = 16_384;
    private const long Length = 1;

    [Test]
    public async Task AcBlob003InvalidOwnerAndProjectMetadataRejectWithoutBlobOrQuotaEffects()
    {
        using var database = new TestDatabase();
        BlobStorageTestSupport.Configure(database);
        var partition = database.Partition;
        var quotaKey = KeyCodec.Encode(QuotaSpace, partition.TenantId, partition.DatabaseId,
            partition.TransactionDomainId, ResourceName);
        var globalKey = KeyCodec.Encode(GlobalSpace);
        var quotaBefore = Read(database, quotaKey);
        var globalBefore = Read(database, globalKey);

        await RejectMetadata(database, (OwnerBlobId, new RowAccess(new string('o', MaximumIdentifierBytes + 1))));
        await RejectMetadata(database, (ProjectBlobId,
            new RowAccess(ProjectId: new string('é', MaximumIdentifierBytes / 2 + 1))));
        await RejectMetadata(database, (OwnerControlBlobId, new RowAccess("owner\u0001")));
        await RejectMetadata(database, (ProjectControlBlobId, new RowAccess(ProjectId: "project\u007f")));

        await Assert.That(Read(database, quotaKey) is { } quotaAfter
            && quotaBefore is not null && quotaAfter.AsSpan().SequenceEqual(quotaBefore)).IsTrue();
        await Assert.That(Read(database, globalKey) is { } globalAfter
            && globalBefore is not null && globalAfter.AsSpan().SequenceEqual(globalBefore)).IsTrue();

        var maxIdentifier = new string('é', MaximumIdentifierBytes / 2);
        await Assert.That(Encoding.UTF8.GetByteCount(maxIdentifier)).IsEqualTo(MaximumIdentifierBytes);
        var validAccess = new RowAccess(maxIdentifier, maxIdentifier);
        await Assert.That(NativeSerialization.Serialize(validAccess).Length <= MaximumAccessRecordBytes).IsTrue();
        var validBlob = new BlobRef(partition, ResourceName, MaximumBlobId);
        var validId = Guid.NewGuid();
        var validRequest = new BeginBlobUploadRequest(validId, validBlob, Guid.NewGuid(), Length, 0, validAccess);
        var healthy = database.Submit(OperationKind.BeginBlobUpload, validRequest, id: validId)
            .Get<BlobCommitResult<BlobUploadInfo>>();
        await Assert.That(healthy.Value.DeclaredLength).IsEqualTo(Length);
        var validStateKey = KeySpace.Partition(StateSpace, validBlob.Partition, validBlob.Resource,
            validBlob.Id, validRequest.UploadId.ToString(GuidFormat));
        var serializedState = Read(database, validStateKey)
            ?? throw new InvalidOperationException("The valid upload state must be persisted.");
        await Assert.That(serializedState.Length <= MaximumAccessRecordBytes).IsTrue();
        var persistedState = NativeSerialization.Deserialize<BlobState>(serializedState);
        await Assert.That(persistedState.Access.OwnerId).IsEqualTo(maxIdentifier);
        await Assert.That(persistedState.Access.ProjectId).IsEqualTo(maxIdentifier);
        var validHeadKey = KeySpace.Partition(HeadSpace, validBlob.Partition, validBlob.Resource, validBlob.Id);
        var serializedHead = Read(database, validHeadKey)
            ?? throw new InvalidOperationException("The valid head metadata must be persisted.");
        await Assert.That(serializedHead.Length <= MaximumAccessRecordBytes).IsTrue();
        var persistedHead = NativeSerialization.Deserialize<BlobHead>(serializedHead);
        await Assert.That(persistedHead.Metadata.Access.OwnerId).IsEqualTo(maxIdentifier);
        await Assert.That(persistedHead.Metadata.Access.ProjectId).IsEqualTo(maxIdentifier);
    }

    private static async Task RejectMetadata(TestDatabase database, (string Id, RowAccess Access) testCase)
    {
        var blob = new BlobRef(database.Partition, ResourceName, testCase.Id);
        var uploadId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var request = new BeginBlobUploadRequest(commandId, blob, uploadId, Length, 0, testCase.Access);
        var result = database.Submit(OperationKind.BeginBlobUpload, request, id: commandId);
        var headKey = KeySpace.Partition(HeadSpace, blob.Partition, blob.Resource, blob.Id);
        var stateKey = KeySpace.Partition(StateSpace, blob.Partition, blob.Resource, blob.Id,
            uploadId.ToString(GuidFormat));

        await Assert.That(result.Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(Read(database, headKey)).IsNull();
        await Assert.That(Read(database, stateKey)).IsNull();
    }

    private static byte[]? Read(TestDatabase database, byte[] key) =>
        database.Store.Read(view => view.ReadOwnedValue(key));
}
