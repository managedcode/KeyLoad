using KeyLoad.Core.BlobStorage;
using ManagedCode.Communication;
using ManagedCode.Storage.Core.Models;
using ManagedCode.Storage.FileSystem;
using ManagedCode.Storage.Server.ChunkUpload;
using ManagedCode.Storage.Server.Controllers;
using ManagedCode.Storage.Server.Extensions.Controller;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KeyLoad.Server;

/// <summary>Composes the Storage server with canonical KeyLoad metadata and fresh authorization.</summary>
[ApiController, Authorize, ServiceFilter(typeof(FileStorageResourceFilter))]
[Route(RootRoute)]
internal sealed partial class FileStorageController(IFileSystemStorage storage, ChunkUploadService chunks, StorageServerOptions options)
    : StorageControllerBase<IFileSystemStorage>(storage, chunks, options)
{
    private const string RootRoute = "api/storage/{tenant}/{database}/{domain}/{partition}/{resource}";
    private const string ChunkRoute = "upload-chunks/{uploadId}";
    private const string MetadataRoute = "metadata/{*path}";
    private const string ListRoute = "list";
    private const string FileRoute = "files/{*path}";
    private const string CleanupRoute = "cleanup";
    private const string AsyncAbortRequired = "Use the authorized asynchronous upload abort action.";

    private FileStorageRequest Files => HttpContext.RequestServices.GetRequiredService<FileStorageRequest>();
    private readonly StorageServerOptions serverOptions = options;

    /// <inheritdoc />
    [FileStorageCapability(Capability.BlobWrite)]
    public override async Task<Result<BlobMetadata>> UploadAsync([FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (serverOptions.EnableFileSizeValidation && file.Length > serverOptions.MaxFileSize)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, FileStorageProtocol.Invalid);
        }
        var reserved = await Files.ReserveAsync(file.FileName, Guid.NewGuid(), file.Length,
            FileStorageNames.ContentType(file.FileName, file.ContentType), cancellationToken);
        return Result<BlobMetadata>.Succeed(await FileUploadPublication.ExecuteAsync(Files, reserved,
            (staged, token) => this.UploadFormFileAsync(Storage, file,
                new UploadOptions(FilePhysicalIdentity.Object(staged.ObjectId), mimeType: staged.ContentType), token), cancellationToken));
    }

    /// <inheritdoc />
    [FileStorageCapability(Capability.BlobWrite)]
    public override async Task<Result<BlobMetadata>> UploadStreamAsync([FromHeader(Name = StorageServerHeaders.FileName)] string fileName,
        [FromHeader(Name = StorageServerHeaders.ContentType)] string? contentType,
        [FromHeader(Name = StorageServerHeaders.Directory)] string? directory, CancellationToken cancellationToken)
    {
        var path = FileStorageNames.Path(fileName, directory);
        var length = FileStorageNames.Length(Request);
        var reserved = await Files.ReserveAsync(path, Guid.NewGuid(), length,
            FileStorageNames.ContentType(path, contentType), cancellationToken);
        var metadata = await FileUploadPublication.ExecuteAsync(Files, reserved, async (staged, token) =>
        {
            var result = await base.UploadStreamAsync(FilePhysicalIdentity.Object(staged.ObjectId), staged.ContentType, null, token);
            return result.IsSuccess ? result.Value! : throw Errors.Fail(ErrorCode.OwnershipLost, FileStorageProtocol.Invalid);
        }, cancellationToken);
        return Result<BlobMetadata>.Succeed(metadata);
    }

    /// <inheritdoc />
    [FileStorageCapability(Capability.BlobRead)]
    public override async Task<ActionResult> DownloadAsync(string path, CancellationToken cancellationToken)
        => await FileDownloadExecution.OpenAsync(this, Files, path, true, cancellationToken);

    /// <inheritdoc />
    [FileStorageCapability(Capability.BlobRead)]
    public override async Task<IActionResult> StreamAsync(string path, CancellationToken cancellationToken)
        => await FileDownloadExecution.OpenAsync(this, Files, path, false, cancellationToken);

    /// <inheritdoc />
    [FileStorageCapability(Capability.BlobRead)]
    public override async Task<ActionResult> DownloadBytesAsync(string path, CancellationToken cancellationToken)
        => await FileDownloadExecution.OpenAsync(this, Files, path, true, cancellationToken);
}
