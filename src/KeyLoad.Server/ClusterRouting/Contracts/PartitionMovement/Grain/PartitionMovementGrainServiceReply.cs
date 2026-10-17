namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>A nonce-bound native result retaining the original signed operation reply.</summary>
[GenerateSerializer, Alias(PartitionMovementGrainServiceProtocol.ReplyAlias)]
internal sealed record PartitionMovementGrainServiceReply(
    [property: Id(PartitionMovementGrainServiceProtocol.ReplyPayload)] ReadOnlyMemory<byte> Payload,
    [property: Id(PartitionMovementGrainServiceProtocol.ReplySignature)] string Signature,
    [property: Id(PartitionMovementGrainServiceProtocol.ReplyError)] ErrorCode? Error,
    [property: Id(PartitionMovementGrainServiceProtocol.ReplySafeDetail)] string? SafeDetail,
    [property: Id(PartitionMovementGrainServiceProtocol.ReplyNonce)] Guid Nonce,
    [property: Id(PartitionMovementGrainServiceProtocol.ReplySenderSignature)] string SenderSignature);
