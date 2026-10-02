namespace KeyLoad.Core.Features.Messaging;

/// <summary>Holds a fenced lease and stored body size without retaining its payload.</summary>
internal readonly record struct ValidatedQueueLease(DeliveryClaims Claims, MessageMetadata Metadata, long BodyBytes);
