namespace KeyLoad.IntegrationTests.Messaging;

internal sealed record QueueRenewedDeadlineSettled(QueueRenewedDeadlinePublicState Original,
    DeliveryCommand Ack, CommitReceipt AckReceipt, MessageInspection Acked);
