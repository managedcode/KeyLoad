using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.TimeSeries;

internal sealed class TimeSeriesChunkQualificationClusterState(PartitionRef[] partitions, Guid window, McpPersistedIdentity reader, string rootCredential)
{
    internal PartitionRef[] Partitions
    {
        get;
    } = partitions;
    internal Guid Window
    {
        get;
    } = window;
    internal string RootCredential
    {
        get;
    } = rootCredential;
    internal McpPersistedIdentity Reader
    {
        get;
    } = reader;
    internal List<(CommandRequest Command, CommitReceipt Receipt)> Originals
    {
        get;
    } = [];
    internal OutboxStatus[] Outboxes
    {
        get;
    } = new OutboxStatus[TimeSeriesChunkQualificationClusterData.PartitionCount];
    internal int Phase
    {
        get;
        set;
    }
    internal long Purged
    {
        get;
        set;
    }
    internal bool HasMore
    {
        get;
        set;
    }
}
