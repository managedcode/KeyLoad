using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed record ReplicaForwardMetadataSample(IReplicaForwardedCommandWork Work,
    ReadExecutionBudget Source, Guid ExpectedCommandId, int Sequence, long SourceBeforeBytes, int SourceBeforeRecords)
{
    internal Guid ActualCommandId
    {
        get; set;
    }
    internal DateTimeOffset ReceiverExpiry
    {
        get; set;
    }
    internal long ReceiverMaximumReadBytes
    {
        get; set;
    }
    internal int ReceiverMaximumRecords
    {
        get; set;
    }
    internal NativeRequestWorkLease Lease => (NativeRequestWorkLease)Work.Binding;
}
