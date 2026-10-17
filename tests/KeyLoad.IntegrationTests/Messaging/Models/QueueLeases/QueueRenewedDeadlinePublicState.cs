namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueueRenewedDeadlinePublicState(QueueLeaseRf3Seed Seed, ReceiveRequest Request,
    ReceiveResult Claim, Delivery Delivery, DeliveryCommand Renew, CommitReceipt RenewReceipt,
    MessageInspection Renewed, List<QueueRenewedDeadlineRefusal> Refusals);

internal sealed record QueueRenewedDeadlineRefusal(CommandRequest Request, ErrorCode Code, string Detail,
    string OriginalProblem);

internal sealed record QueueRenewedDeadlineSettled(QueueRenewedDeadlinePublicState Original,
    DeliveryCommand Ack, CommitReceipt AckReceipt, MessageInspection Acked);
