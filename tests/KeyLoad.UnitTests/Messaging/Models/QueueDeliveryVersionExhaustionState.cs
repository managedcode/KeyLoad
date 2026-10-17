namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueDeliveryVersionExhaustionState(QueueClaimVersionExhaustionState Data,
    ReplicatedOperation Claim, OperationResult Claimed, Delivery Delivery, MessageMetadata RepairedMetadata);
