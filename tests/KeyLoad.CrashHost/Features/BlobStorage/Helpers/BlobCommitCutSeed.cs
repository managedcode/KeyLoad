using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost.Features.BlobStorage;

internal static class BlobCommitCutSeed
{
    private const string DeniedPrincipalCommandIdText = "e4231c59-b672-4e61-96de-42c85fcb8561";
    private const string ConfigureIdText = "aab065aa-79b4-4a6b-8d2c-7d4ef3737e47";
    private const string OriginalCompleteIdText = "1d2e2a9e-d412-4b3e-a114-fc1a8265fba6";
    private const string OriginalBeginIdText = "2e5d77a4-27d6-40bd-8684-c721dc0945b4";
    private const string NewBeginIdText = "ce4d372a-d8e6-4733-a0b9-9e215924cb56";
    private const string OriginalPartIdText = "273cb0c0-b639-449e-b3c4-66472c77bbbf";
    private const string NewFirstPartIdText = "0df2fe1f-ed6b-4bce-b3d6-3e00bd9c9672";
    private const string NewSecondPartIdText = "34ba2f77-e082-4f13-bb0b-ec7512c7056d";
    private const string SeedSidecarExceeded = "The original blob seed sidecar exceeded its bound.";
    private const string SeedRejected = "The genuine blob seed operation was rejected.";
    private const string SeedImageIncomplete = "The bounded blob seed image is incomplete.";
    private const long OriginalRevision = 0;
    private const long PublishedRevision = 1;
    private const int FirstPartOffset = 0;
    private const int FirstPartOrdinal = 0;

    internal static async Task<DatabaseEngine> WriteAsync(string root, ZoneTreeStore store, bool complete)
    {
        var database = CrashDatabase.Create(store);
        var operations = new List<byte[]>();
        var results = new List<byte[]>();
        Submit(database, OperationKind.ConfigurePrincipal, Guid.Parse(DeniedPrincipalCommandIdText),
            new ConfigurePrincipalRequest(new(BlobCommitCutProtocol.DeniedPrincipal,
                BlobCommitCutProtocol.Partition.TenantId, [], [])), operations, results);
        Submit(database, OperationKind.ConfigureResource, Guid.Parse(ConfigureIdText),
            new ConfigureResourceRequest(BlobCommitCutProtocol.Partition.TenantId, BlobCommitCutProtocol.Partition.DatabaseId,
                new(BlobCommitCutProtocol.Resource, ResourceKind.BlobStore, BlobCommitCutProtocol.Partition.TransactionDomainId)), operations, results);
        var originalHash = Upload(database, BlobCommitCutProtocol.OriginalUpload, BlobCommitCutPayload.OriginalBytes(), OriginalRevision, operations, results);
        var id = Guid.Parse(OriginalCompleteIdText);
        Submit(database, OperationKind.CompleteBlobUpload, id,
            new CompleteBlobUploadRequest(id, BlobCommitCutProtocol.Blob, BlobCommitCutProtocol.OriginalUpload, originalHash), operations, results);
        if (complete)
        { _ = Upload(database, BlobCommitCutProtocol.NewUpload, BlobCommitCutPayload.MultipartBytes(), PublishedRevision, operations, results); }
        var identity = store.Identity;
        var state = new
        {
            identity.NodeId,
            identity.Incarnation,
            identity.ReadGeneration,
            store.Position,
            Catalog = NativeSerialization.Serialize(database.ReadPhysicalShardCatalog(BlobCommitCutProtocol.Principal)),
            PrincipalBytes = store.Read(view => view.ReadOwnedValue(KeySpace.Principal(BlobCommitCutProtocol.Principal))),
            DeniedPrincipalBytes = store.Read(view => view.ReadOwnedValue(KeySpace.Principal(BlobCommitCutProtocol.DeniedPrincipal))),
            Operations = operations.ToArray(),
            Results = results.ToArray(),
            RawImage = Image(store)
        };
        var bytes = JsonDefaults.Serialize(state);
        if (bytes.Length > BlobCommitCutProtocol.MaximumSidecarBytes)
        { throw new InvalidOperationException(SeedSidecarExceeded); }
        await File.WriteAllBytesAsync(Path.Combine(root, BlobCommitCutProtocol.SeedFile), bytes).ConfigureAwait(false);
        return database;
    }

    private static string Upload(DatabaseEngine database, Guid upload, byte[] bytes, long revision,
        List<byte[]> operations, List<byte[]> results)
    {
        var begin = upload == BlobCommitCutProtocol.OriginalUpload
            ? Guid.Parse(OriginalBeginIdText) : Guid.Parse(NewBeginIdText);
        Submit(database, OperationKind.BeginBlobUpload, begin,
            new BeginBlobUploadRequest(begin, BlobCommitCutProtocol.Blob, upload, bytes.Length, revision), operations, results);
        var integrity = BlobIntegrity.InitialHash(database.Store.Identity.Incarnation, BlobCommitCutProtocol.Blob, upload, bytes.Length);
        for (var offset = FirstPartOffset; offset < bytes.Length; offset += BlobCommitCutProtocol.PartLength)
        {
            var ordinal = offset / BlobCommitCutProtocol.PartLength;
            var part = bytes.AsMemory(offset, Math.Min(BlobCommitCutProtocol.PartLength, bytes.Length - offset));
            var digest = Convert.ToHexStringLower(SHA256.HashData(part.Span));
            var id = upload == BlobCommitCutProtocol.OriginalUpload ? Guid.Parse(OriginalPartIdText)
                : ordinal == FirstPartOrdinal ? Guid.Parse(NewFirstPartIdText) : Guid.Parse(NewSecondPartIdText);
            Submit(database, OperationKind.WriteBlobPart, id,
                new WriteBlobPartRequest(id, BlobCommitCutProtocol.Blob, upload, ordinal, part, digest), operations, results);
            integrity = BlobIntegrity.NextHash(integrity, ordinal, part.Length, digest);
        }
        return integrity;
    }

    private static void Submit<T>(DatabaseEngine database, OperationKind kind, Guid id, T request,
        List<byte[]> operations, List<byte[]> results)
    {
        var operation = database.CreateNativeOperation(kind, id, BlobCommitCutProtocol.Principal,
            database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        var result = database.Apply(operation);
        if (result.Error is not null)
        { throw new InvalidOperationException(SeedRejected); }
        operations.Add(NativeSerialization.Serialize(operation));
        results.Add(NativeSerialization.Serialize(result));
    }

    private static string Image(ZoneTreeStore store) => store.Read(view =>
    {
        var page = view.Scan([], BlobCommitCutProtocol.MaximumRecords);
        if (page.HasMore)
        { throw new InvalidOperationException(SeedImageIncomplete); }
        return Convert.ToHexString(JsonDefaults.Serialize(page.Records));
    });
}
