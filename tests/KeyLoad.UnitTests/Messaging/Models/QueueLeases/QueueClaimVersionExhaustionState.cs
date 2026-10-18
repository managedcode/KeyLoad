namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueClaimVersionExhaustionState(QueueLaneRef Lane, DateTimeOffset Time,
    CommandRequest Producer, global::ManagedCode.Communication.Result<object> Produced,
    byte[] MetadataKey, byte[] OriginalMetadata, MessageMetadata DamagedMetadata, MessageBody Body,
    global::ManagedCode.Communication.Commands.Command<DatabaseCommand> Operation);
