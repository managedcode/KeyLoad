namespace KeyLoad.ClusterTests.Messaging;

internal sealed record QueueProducerClusterHealthy(QueueProducerClusterOriginal Original,
    CommandRequest Command, CommitReceipt Receipt, DeliveryCommand Ack, CommitReceipt AckReceipt,
    MessageInspection Acked, QueueProducerClusterImage Image);
