namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueueRenewedDeadlinePublicState(QueueLeaseRf3Seed Seed, ReceiveRequest Request,
    ReceiveResult Claim, Delivery Delivery, DeliveryCommand Renew, CommitReceipt RenewReceipt,
    MessageInspection Renewed, List<QueueRenewedDeadlineRefusal> Refusals);
