using System.Collections.Immutable;

namespace KeyLoad.Core.BlobStorage;

[Orleans.GenerateSerializer, Orleans.Alias(FileStorageProtocol.QueryAlias)]
internal sealed record FileMetadataQuery([property: Orleans.Id(0)] FileAddress Address,
    [property: Orleans.Id(1)] Capability Capability, [property: Orleans.Id(2)] Guid UploadId,
    [property: Orleans.Id(3)] bool List = false, [property: Orleans.Id(4)] string? After = null,
    [property: Orleans.Id(5)] int Limit = FileStorageProtocol.DefaultPageSize);

[Orleans.GenerateSerializer, Orleans.Alias(FileStorageProtocol.StateAlias)]
internal sealed record FileMetadataState([property: Orleans.Id(0)] ResourceIncarnation Incarnation,
    [property: Orleans.Id(1)] FileEntry? Entry, [property: Orleans.Id(2)] FileUpload? Upload,
    [property: Orleans.Id(3)] ImmutableArray<FileEntry> Entries, [property: Orleans.Id(4)] bool HasMore = false,
    [property: Orleans.Id(5)] ImmutableArray<FileUpload> Uploads = default,
    [property: Orleans.Id(6)] string? NextAfter = null);
