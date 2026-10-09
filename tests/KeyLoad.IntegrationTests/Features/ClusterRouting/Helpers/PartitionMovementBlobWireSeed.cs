using System.Globalization;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class PartitionMovementBlobWireSeed(PartitionMovementBlobWireCallers callers)
{
    private const string Resource = "wire-blobs";
    private const string BlobId = "wire-original";
    private const string DocumentResource = "wire-documents";
    private const string DocumentId = "original";
    private const string DocumentJson = "{\"wire\":\"native-original\"}";
    private const long InitialRevision = 0;
    private const int CurrentProtocol = 1;
    private const int FirstOrdinal = 0;
    private const int OriginalLiveBlobCount = 1;
    private const int Length = 8;
    private const byte Content = 0x47;
    internal PartitionRef Partition { get; } = new("protected-parent-tenant", "wire-database", "wire-domain", Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture));
    internal PartitionMoveRequest Request { get; private set; } = null!;
    internal BlobMetadata Metadata { get; private set; } = null!;
    private readonly List<Func<CancellationToken, Task>> replays = [];

    internal async Task CreateAsync(PartitionMovementLateNativeSettings settings, CancellationToken token)
    {
        foreach (var definition in new[] { new ResourceDefinition(Resource, ResourceKind.BlobStore, Partition.TransactionDomainId),
            new ResourceDefinition(DocumentResource, ResourceKind.Collection, Partition.TransactionDomainId) })
        { _ = await McpCallerAssertions.SdkSuccessAsync(await callers.Source.ConfigureResourceAsync(Guid.NewGuid(),
            new(Partition.TenantId, Partition.DatabaseId, definition), token)); }
        var placement = await McpCallerAssertions.SdkSuccessAsync(await callers.Source.ReadAtomicPartitionPlacementAsync(new(CurrentProtocol, Partition), token));
        var command = new CommandRequest(Guid.NewGuid(), Partition,
            [new PutDocument(DocumentResource, DocumentId, DocumentJson)], placement.PlacementEpoch);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await callers.Source.CommitAsync(command, token));
        replays.Add(ct => callers.RequireAsync(Partition, McpCallerTools.DocumentsCommit, command,
            value => callers.Source.CommitAsync(command, value), receipt, ct));
        var blob = new BlobRef(Partition, Resource, BlobId);
        var upload = Guid.NewGuid();
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, upload, Length, InitialRevision);
        var begun = await McpCallerAssertions.SdkSuccessAsync(await callers.Source.BeginBlobUploadAsync(begin, token));
        replays.Add(ct => callers.RequireAsync(Partition, BlobToolNames.BeginUpload, begin,
            value => callers.Source.BeginBlobUploadAsync(begin, value), begun, ct));
        var bytes = Enumerable.Repeat(Content, Length).ToArray();
        var part = new WriteBlobPartRequest(Guid.NewGuid(), blob, upload, FirstOrdinal, bytes, BlobIntegrity.PartHash(bytes));
        var written = await McpCallerAssertions.SdkSuccessAsync(await callers.Source.WriteBlobPartAsync(part, token));
        replays.Add(ct => callers.RequireAsync(Partition, BlobToolNames.WritePart, part,
            value => callers.Source.WriteBlobPartAsync(part, value), written, ct));
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, upload,
            BlobIntegrity.NextHash(begun.Value.IntegrityHash, part.Ordinal, part.Bytes.Length, part.Sha256));
        var completed = await McpCallerAssertions.SdkSuccessAsync(await callers.Source.CompleteBlobUploadAsync(complete, token));
        Metadata = completed.Value;
        replays.Add(ct => callers.RequireAsync(Partition, BlobToolNames.CompleteUpload, complete,
            value => callers.Source.CompleteBlobUploadAsync(complete, value), completed, ct));
        Request = new(Guid.NewGuid(), Partition, settings.Destination, placement.Revision, PartitionMoveMode.Transfer);
    }

    internal async Task RequireAsync(CancellationToken token)
    {
        foreach (var replay in replays) { await replay(token); }
        var document = await McpCallerAssertions.SdkSuccessAsync(await callers.Source.GetAsync(new(Partition, DocumentResource, DocumentId), token));
        await Assert.That(document!.Json).IsEqualTo(DocumentJson);
        var bytes = Enumerable.Repeat(Content, Length).ToArray();
        var expected = new BlobReadResult(Metadata, InitialRevision, bytes);
        var read = new BlobReadRequest(Metadata.Blob, Metadata.Revision, InitialRevision, Length);
        await callers.RequireAsync(Partition, BlobToolNames.ReadRange, read, ct => callers.Source.ReadBlobRangeAsync(read, ct), expected, token);
        var metadata = new BlobMetadataRequest(Metadata.Blob);
        await callers.RequireAsync(Partition, BlobToolNames.Metadata, metadata,
            ct => callers.Source.GetBlobMetadataAsync(metadata, ct), Metadata, token);
        var upload = new BlobUploadInfoRequest(Metadata.Blob, Metadata.VersionId!.Value);
        var original = await McpCallerAssertions.SdkSuccessAsync(await callers.Source.GetBlobUploadInfoAsync(upload, token));
        await callers.RequireAsync(Partition, BlobToolNames.UploadInfo, upload,
            ct => callers.Source.GetBlobUploadInfoAsync(upload, ct), original, token);
        var list = new BlobListRequest(Partition, Resource);
        var page = await McpCallerAssertions.SdkSuccessAsync(await callers.Source.ListBlobsAsync(list, token));
        await Assert.That(page.Items.Length).IsEqualTo(OriginalLiveBlobCount);
        await SqlRf3Protocol.EqualAsync(Metadata, page.Items.Single(value => value.Blob == Metadata.Blob));
        await callers.RequireAsync(Partition, BlobToolNames.List, list, ct => callers.Source.ListBlobsAsync(list, ct), page, token);
    }
}
