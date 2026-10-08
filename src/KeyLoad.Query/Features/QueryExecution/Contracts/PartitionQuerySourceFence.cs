namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Retains only local current-source authority and a digest of its actual persisted resource bytes.</summary>
internal sealed record PartitionQuerySourceFence(RemoteDocumentReadFenceV1 Authority, string ResourceDigest);
