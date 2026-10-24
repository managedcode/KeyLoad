using KeyLoad.Core.BlobStorage;
using ManagedCode.MimeTypes;

namespace KeyLoad.Server;

internal static class FileStorageNames
{
    internal static string Path(string name, string? directory)
        => string.IsNullOrEmpty(directory) ? name : string.Join(FileStorageProtocol.PathSeparator, directory, name);

    internal static string ContentType(string name, string? contentType)
        => string.IsNullOrWhiteSpace(contentType) ? MimeHelper.GetMimeType(name) : contentType;

    internal static long Length(HttpRequest request)
    {
        var declared = request.Headers[FileStorageHeaders.Length].FirstOrDefault();
        var length = declared is null ? request.ContentLength
            : long.TryParse(declared, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        return length is >= FileStorageProtocol.EmptyCount ? length.Value : throw Errors.Fail(ErrorCode.Validation, FileStorageProtocol.Invalid);
    }
}
