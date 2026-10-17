using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Retains genuine parent producer evidence without minting a child permit or public result.</summary>
internal sealed record ControlledPartitionMovementProcessPrepared(
    ControlledPartitionMovementProcessInput Input,
    DateTimeOffset RecordedAt,
    ReplicatedOperation OriginalSeed,
    byte[] OriginalReceipt,
    OperationResult OriginalBlobOutcome,
    CompleteBlobUploadRequest OriginalBlob,
    DateTimeOffset BlobEvaluatedAt,
    PartitionMovePhaseResult Prepared,
    PartitionMovePhaseResult Authorization,
    ServerRuntimeOptions Runtime,
    string OriginalCallerAddress,
    DateTimeOffset OriginalExpiry,
    string[] UntouchedTargetImage);
