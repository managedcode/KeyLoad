using KeyLoad.UnitTests.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class QueueViewLeaseIsolationState(DocumentPatchAtomicityOwner owner, CancellationToken token)
{
    internal CancellationToken Token { get; } = token;
    internal DocumentPatchAtomicityOwner Owner { get; } = owner;
    internal PartitionRef Partition => Owner.Source.Partition;
    internal QueueLaneRef Lane => new(Partition, QueueViewLeaseIsolationProtocol.Queue);
    internal PrincipalRecord Reader
    {
        get; set;
    } = new(QueueViewLeaseIsolationProtocol.Reader,
        owner.Source.Partition.TenantId, [], []);
    internal List<QueueViewLeaseIsolationHistory> History { get; } = [];
    internal QueueViewLeaseIsolationHistory? Claim
    {
        get; set;
    }
    internal DateTimeOffset CurrentDeadline
    {
        get; set;
    }
    internal bool Acked
    {
        get; set;
    }
    internal bool Healthy
    {
        get; set;
    }
    internal long Version { get; set; } = QueueViewLeaseIsolationProtocol.Leased;
    internal Delivery Delivery => Claim?.Result.Get<ReceiveResult>().Deliveries.Single()
        ?? throw new InvalidOperationException(QueueViewLeaseIsolationProtocol.Absent);
}
