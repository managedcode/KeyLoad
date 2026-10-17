
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueuePolicyEpochRf3Known(DeliveryCommand Command, long PolicyEpoch,
    CommitReceipt? Receipt, ErrorCode? Error);
