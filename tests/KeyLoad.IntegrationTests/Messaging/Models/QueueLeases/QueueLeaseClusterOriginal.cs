namespace KeyLoad.IntegrationTests.Messaging;

internal sealed record QueueLeaseClusterOriginal(QueueLeaseClusterSeed Seed,
    ReceiveRequest Claim, ReceiveResult Claimed, Delivery Delivery,
    DeliveryCommand Renew, CommitReceipt RenewReceipt,
    DeliveryCommand Nack, CommitReceipt NackReceipt, MessageInspection Scheduled);
