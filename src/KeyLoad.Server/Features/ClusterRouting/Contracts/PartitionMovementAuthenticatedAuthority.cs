namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Original emitted native control read proof; source verification remains mandatory.</summary>
internal sealed record PartitionMovementAuthenticatedAuthority(byte[] ReplyBytes, string Signature);
