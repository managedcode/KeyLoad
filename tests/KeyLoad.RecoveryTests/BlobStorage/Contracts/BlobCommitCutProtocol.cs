namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobCommitCutProtocol
{
    private const string Tenant = "system";
    private const string Database = "database";
    private const string TransactionDomain = "blob-cut";
    private const string PartitionKey = "partition";
    private const string OriginalUploadText = "7e7b65b4-2a2d-46d6-8ca8-cdf127fb0caa";
    private const string NewUploadText = "3025642a-e739-42f2-92d7-c6a631458d48";
    private const string CompleteIdText = "eb6eb941-42f7-4729-a6ea-6e2f4bc1fb42";
    private const string DeleteIdText = "9b856242-5aba-4397-8640-75f76d294f3b";

    internal const string UnknownMarker = "blob-unknown-write-outcome";
    internal const string CompleteMode = "blob-complete-commit-cut";
    internal const string DeleteMode = "blob-delete-commit-cut";
    internal const string SeedFile = "blob-commit-cut-seed.json";
    internal const string OperationFile = "blob-commit-cut-operation.native";
    internal const string ReturnedFile = "blob-commit-cut-returned.native";
    internal const string FaultFile = "blob-commit-cut-fault.json";
    internal const string Principal = "root";
    internal const string DeniedPrincipal = "blob-denied";
    internal const string UnknownDetail = "The transaction outcome is unknown. Recover and retry the same command ID.";
    internal const string PoisonDetail = "The database must recover before accepting another operation.";
    internal const string Resource = "files";
    internal const string BlobId = "selected";
    internal const int PartLength = 65_536;
    internal const int MultipartLength = 65_553;
    internal const int MaximumRecords = 4_096;
    internal const int MaximumSidecarBytes = 16_777_216;
    internal static readonly PartitionRef Partition = new(Tenant, Database, TransactionDomain, PartitionKey);
    internal static readonly BlobRef Blob = new(Partition, Resource, BlobId);
    internal static readonly Guid OriginalUpload = Guid.Parse(OriginalUploadText);
    internal static readonly Guid NewUpload = Guid.Parse(NewUploadText);
    internal static readonly Guid CompleteId = Guid.Parse(CompleteIdText);
    internal static readonly Guid DeleteId = Guid.Parse(DeleteIdText);
}
