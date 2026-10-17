namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueueLeaseRf3Original(QueueLeaseRf3Seed Seed,
    ReceiveRequest Claim, ReceiveResult Claimed, Delivery Delivery,
    DeliveryCommand Renew, CommitReceipt RenewReceipt,
    DeliveryCommand Nack, CommitReceipt NackReceipt, MessageInspection Scheduled);
