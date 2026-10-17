using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class AtomicPartitionScopeLifecycleState(PartitionRef left, StoreIdentity originalIdentity)
{
    internal PartitionRef Left
    {
        get;
    } = left;
    internal PartitionRef Right
    {
        get;
    } = left with
    {
        PartitionKey = "customer-2"
    };
    internal PartitionRef Foreign
    {
        get;
    } = left with
    {
        TransactionDomainId = "foreign-domain"
    };
    internal StoreIdentity OriginalIdentity
    {
        get;
    } = originalIdentity;
    internal Guid Incarnation => OriginalIdentity.Incarnation;
    internal Dictionary<string, DateTimeOffset> EventTimes
    {
        get;
    } = [];
    internal bool[] Present
    {
        get;
    } = new bool[2];
    internal bool[] Healthy
    {
        get;
    } = new bool[2];
    internal string?[] Outbox
    {
        get;
    } = new string?[2];
    internal List<(CommandRequest Request, OperationResult Result)> Originals
    {
        get;
    } = [];
    internal PartitionRef Partition(int index) => index == 0 ? Left : Right;
}
