namespace KeyLoad.ClusterTests.Messaging;

internal sealed record QueueRenewedDeadlineSettled(QueueRenewedDeadlinePublicState Original,
    DeliveryCommand Ack, CommitReceipt AckReceipt, MessageInspection Acked);
