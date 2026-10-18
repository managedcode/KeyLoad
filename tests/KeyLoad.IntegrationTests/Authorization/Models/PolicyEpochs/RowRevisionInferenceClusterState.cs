namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed class RowRevisionInferenceClusterState(PartitionRef partition, PrincipalRecord reader, string key)
{
    internal PartitionRef Partition
    {
        get;
    } = partition;
    internal PrincipalRecord Reader
    {
        get;
    } = reader;
    internal string Key
    {
        get;
    } = key;
    internal long HiddenRevision
    {
        get;
        set;
    } = RowRevisionInferenceClusterProtocol.OriginalRevision;
    internal bool HiddenDeleted
    {
        get;
        set;
    }
    internal string HiddenJson
    {
        get;
        set;
    } = RowRevisionInferenceClusterProtocol.HiddenJson;
    internal OutboxStatus Outbox
    {
        get;
        set;
    } = null!;
    internal List<(CommandRequest Command, CommitReceipt Receipt)> RootOriginals
    {
        get;
    } = [];
    internal List<(CommandRequest Command, ErrorCode Code, string Detail)> Refused
    {
        get;
    } = [];
    internal (CommandRequest Command, CommitReceipt Receipt) HealthyOriginal
    {
        get;
        set;
    }
}
