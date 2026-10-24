namespace KeyLoad.Core.BlobStorage;

internal static class FileStorageProtocol
{
    internal const string GuidFormat = "N";
    internal const char PathSeparator = '/';
    internal const char ForbiddenSeparator = '\\';
    internal const string EmptyPath = "";
    internal const string CurrentSegment = ".";
    internal const string ParentSegment = "..";
    internal const string BoundaryOnly = "File metadata requires the file server boundary.";
    internal const int EmptyCount = 0;
    internal const int FirstRecordCount = 1;
    internal const long InitialRevision = 0;
    internal const long RevisionStep = 1;
    internal const int DefaultPageSize = 32;
    internal const string AddressAlias = "keyload.file.address.v1";
    internal const string MutationAlias = "keyload.file.metadata-mutation.v1";
    internal const string EntryAlias = "keyload.file.entry.v1";
    internal const string UploadAlias = "keyload.file.upload.v1";
    internal const string QueryAlias = "keyload.file.query.v1";
    internal const string StateAlias = "keyload.file.state.v1";
    internal const string QuotaFamily = "file-quota";
    internal const string HeadFamily = "file-head";
    internal const string UploadFamily = "file-upload";
    internal const string Invalid = "The file metadata or physical object identity is invalid.";
    internal const string Denied = "The current principal cannot access this file upload.";
    internal const int MaximumPathCharacters = 1024;
    internal const int MaximumContentTypeCharacters = 256;
    internal const int Sha256Characters = 64;
}
