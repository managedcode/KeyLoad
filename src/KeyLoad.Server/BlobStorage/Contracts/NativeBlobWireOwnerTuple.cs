namespace KeyLoad.Server.Features.BlobStorage;

internal sealed record NativeBlobWireOwnerTuple(Guid PhysicalShardId, Guid Incarnation, string[] VoterIds);
