using KeyLoad.Core.Features.BlobStorage;
using ManagedCode.Storage.Server.Controllers;
using ManagedCode.Storage.Server.Models;

namespace KeyLoad.Server;

internal static class FileChunkAdmission
{
    private const int EmptyLength = 0;
    private const int FirstChunk = 1;

    internal static void Validate(FileUploadPayload payload, StorageServerOptions options)
    {
        if (payload?.File is null || payload.Payload is not { FileSize: >= EmptyLength } value || value.TotalChunks < FirstChunk
            || value.ChunkSize < FirstChunk || value.ChunkIndex < FirstChunk || value.ChunkIndex > value.TotalChunks
            || payload.File.Length > options.MaxChunkSize)
        {
            throw Errors.Fail(ErrorCode.Validation, FileStorageProtocol.Invalid);
        }
        var expectedCount = value.FileSize.Value == EmptyLength ? FirstChunk : (value.FileSize.Value - FirstChunk) / value.ChunkSize + FirstChunk;
        var offset = checked((long)(value.ChunkIndex - FirstChunk) * value.ChunkSize);
        var expectedLength = Math.Min(value.ChunkSize, value.FileSize.Value - offset);
        if (expectedCount != value.TotalChunks || expectedLength < EmptyLength || payload.File.Length != expectedLength)
        {
            throw Errors.Fail(ErrorCode.Validation, FileStorageProtocol.Invalid);
        }
    }
}
