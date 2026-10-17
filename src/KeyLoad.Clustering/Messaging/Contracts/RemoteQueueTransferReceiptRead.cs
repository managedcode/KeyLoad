namespace KeyLoad.Clustering;

internal sealed record RemoteQueueTransferReceiptRead(bool Routed, QueueTransferReceiptInspection? Receipt);
