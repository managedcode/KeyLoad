namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Publishes real current-format blob input through the original native replica owner.</summary>
internal static class ControlledPartitionMovementProcessBlobSeed
{
    private const string UploadIdText = "3836909a-2089-4e20-bbab-788a36df023c";
    private const string ConfigureIdText = "a3aa5b55-c605-4c3b-9e49-871b876ef608";
    private const string BeginIdText = "28175434-9a57-434e-b201-b376c9206f2d";
    private const string PartIdText = "5738caef-a9e4-4623-ad93-d91e3838aff6";
    private const string CompleteIdText = "7eeb58f9-989c-4a0a-9786-038ab9a8a1a7";
    private const byte FirstPayloadByte = 1;
    private const byte SecondPayloadByte = 2;
    private const byte ThirdPayloadByte = 3;
    private const byte FourthPayloadByte = 4;
    internal const string Resource = "movement-files";
    internal const string Id = "evidence-1";
    internal const string PartSha256 = "9f64a747e1b97f131fabb6b447296c9b6f0201e79fb3c5356e6c77e89b6a806a";
    private const int FirstOrdinal = 0;
    private const int InitialRevision = 0;
    private const string PrincipalId = "root";
    private const string Failed = "The native movement blob seed failed.";
    internal static readonly Guid UploadId = Guid.Parse(UploadIdText);
    private static readonly Guid ConfigureId = Guid.Parse(ConfigureIdText);
    private static readonly Guid BeginId = Guid.Parse(BeginIdText);
    private static readonly Guid PartId = Guid.Parse(PartIdText);
    internal static readonly Guid CompleteId = Guid.Parse(CompleteIdText);
    internal static readonly BlobRef Blob = new(ControlledPartitionMovementProcessCorpus.Partition, Resource, Id);
    internal static byte[] Bytes() => [FirstPayloadByte, SecondPayloadByte, ThirdPayloadByte, FourthPayloadByte];

    internal static (CompleteBlobUploadRequest Request, OperationResult Result, DateTimeOffset EvaluatedAt)
        Publish(ControlledPartitionMovementNativeNode source, CancellationToken cancellationToken)
    {
        var partition = ControlledPartitionMovementProcessCorpus.Partition;
        Submit(source, OperationKind.ConfigureResource, ConfigureId,
            new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId,
                new(Resource, ResourceKind.BlobStore, partition.TransactionDomainId)), cancellationToken);
        var bytes = Bytes();
        Submit(source, OperationKind.BeginBlobUpload, BeginId,
            new BeginBlobUploadRequest(BeginId, Blob, UploadId, bytes.Length, InitialRevision), cancellationToken);
        Submit(source, OperationKind.WriteBlobPart, PartId,
            new WriteBlobPartRequest(PartId, Blob, UploadId, FirstOrdinal, bytes, PartSha256), cancellationToken);
        var initial = BlobIntegrity.InitialHash(source.Store.Identity.Incarnation, Blob, UploadId, bytes.Length);
        var integrity = BlobIntegrity.NextHash(initial, FirstOrdinal, bytes.Length, PartSha256);
        var request = new CompleteBlobUploadRequest(CompleteId, Blob, UploadId, integrity);
        var evaluatedAt = source.Database.EvaluationClock.GetUtcNow();
        var original = source.Database.CreateNativeOperation(OperationKind.CompleteBlobUpload, CompleteId,
            PrincipalId, evaluatedAt, NativeSerialization.Serialize(request));
        var result = source.Journal.Submit(original, cancellationToken);
        Require(result);
        return (request, result, evaluatedAt);
    }

    private static void Submit<T>(ControlledPartitionMovementNativeNode source, OperationKind kind,
        Guid commandId, T request, CancellationToken cancellationToken)
    {
        var original = source.Database.CreateNativeOperation(kind, commandId,
            PrincipalId, source.Database.EvaluationClock.GetUtcNow(),
            NativeSerialization.Serialize(request));
        Require(source.Journal.Submit(original, cancellationToken));
    }

    private static void Require(OperationResult result)
    {
        if (result.Error is not null)
        { throw new InvalidOperationException(Failed); }
    }
}
