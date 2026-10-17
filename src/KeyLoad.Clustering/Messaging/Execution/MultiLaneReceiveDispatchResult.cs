namespace KeyLoad.Clustering;

internal sealed record MultiLaneReceiveDispatchResult(GrainOperationReply? Reply, KeyLoadException? Stopped);
