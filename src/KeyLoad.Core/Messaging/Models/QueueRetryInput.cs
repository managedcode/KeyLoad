namespace KeyLoad.Core.Features.Messaging;

internal sealed record QueueRetryInput(QueueLaneRef Lane, QueuePolicy Policy, MessageMetadata Metadata,
    ReadOnlyMemory<byte> DueIndexKey);
