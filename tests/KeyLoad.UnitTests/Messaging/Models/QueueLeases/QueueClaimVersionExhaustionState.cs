namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueClaimVersionExhaustionState(QueueLaneRef Lane, DateTimeOffset Time,
    CommandRequest Producer, global::KeyLoad.OperationResult Produced,
    byte[] MetadataKey, byte[] OriginalMetadata, MessageMetadata DamagedMetadata, MessageBody Body,
    global::KeyLoad.ReplicatedOperation Operation);
