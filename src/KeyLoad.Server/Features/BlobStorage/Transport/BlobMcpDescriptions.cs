namespace KeyLoad.Server;

/// <summary>Agent guidance for canonical upload, bounded reads and explicit cleanup semantics.</summary>
internal static class BlobMcpDescriptions
{
    private const string BeginUpload = "Reserve bounded capacity for one scoped upload using a stable command ID, caller-generated upload ID and expected head revision. An upload is invisible until complete.";
    private const string WritePart = "Append one contiguous raw part of at most 65536 bytes with its lowercase SHA256. Repeat an ordinal only with identical bytes and hash; nonfinal parts have exactly 65536 bytes.";
    private const string CompleteUpload = "Publish a fully accepted upload through head revision CAS using its expected sha256-chain-v1 integrity hash. The chain is distinct from whole-file SHA256.";
    private const string AbortUpload = "Abort a creator-authorized upload and release unused reservation once. Published bytes remain intact; use bounded reclaim to remove accepted parts.";
    private const string Delete = "Publish a tombstone at the required current positive revision. Retained version bytes remain charged until bounded reclaim.";
    private const string Reclaim = "Reclaim at most 128 parts of one authorized aborted, expired or retired upload/version. Current published or unexpired active versions cannot be reclaimed; repeat with a new command ID while parts remain.";
    private const string Metadata = "Read authorized current blob metadata without raw bytes. Absent or unpublished names return null; deleted names return a positive-revision tombstone.";
    private const string UploadInfo = "Read creator-authorized upload progress and its current sha256-chain-v1 hash without raw bytes. Absent uploads return null; upload IDs do not establish authority.";
    private const string ReadRange = "Read an exact authorized range of at most 65536 bytes at one required positive revision. Keep the revision for subsequent calls; overwrite can cause RevisionConflict.";
    private const string List = "List at most 100 authorized live blob metadata rows in canonical key order. Continue using nextAfterId from the last visited row, including empty pages with an advancing cursor.";

    internal static string For(string name) => name switch
    {
        BlobToolNames.BeginUpload => BeginUpload,
        BlobToolNames.WritePart => WritePart,
        BlobToolNames.CompleteUpload => CompleteUpload,
        BlobToolNames.AbortUpload => AbortUpload,
        BlobToolNames.Delete => Delete,
        BlobToolNames.Reclaim => Reclaim,
        BlobToolNames.Metadata => Metadata,
        BlobToolNames.UploadInfo => UploadInfo,
        BlobToolNames.ReadRange => ReadRange,
        BlobToolNames.List => List,
        _ => throw new ArgumentException(McpCatalogProtocol.UnknownDescription, nameof(name))
    };
}
