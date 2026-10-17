namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Ephemeral transport reply scope; native authority remains in the actual signed request and stored outcome.</summary>
internal sealed record PartitionMovementReceiverExchange(PartitionMovementTransportRequest ReplyScope,
    string OriginalPhaseIdentityDigest, string RequestSignature, bool Query);
