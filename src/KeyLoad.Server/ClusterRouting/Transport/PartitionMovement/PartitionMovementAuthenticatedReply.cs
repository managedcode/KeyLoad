namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Retains exact verified wire bytes; reserialization cannot create an original Capture witness.</summary>
internal sealed record PartitionMovementAuthenticatedReply(PartitionMovementTransportReply Value,
    ReadOnlyMemory<byte> OriginalBytes, string Signature);
