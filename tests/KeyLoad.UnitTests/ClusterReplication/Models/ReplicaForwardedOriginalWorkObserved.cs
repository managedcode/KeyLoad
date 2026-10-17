using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class ReplicaForwardedOriginalWorkObserved(IReplicaForwardedCommandWork work)
{
    internal IReplicaForwardedCommandWork Work { get; } = work;
    internal ReadExecutionBudget Budget { get; } = work.Budget;
    internal IResourceLifetimeWorkBinding Binding { get; } = work.Binding;
    internal ResourceLifetimeWorkResultRetention Ledger { get; } = work.Binding.ResultRetention;
    internal long CreatedReadBytes { get; } = work.Budget.ReadBytes;
    internal int Sequence
    {
        get; init;
    }
    internal Guid CommandId
    {
        get; set;
    }
    internal long BeforeExecuteReadBytes
    {
        get; set;
    }
    internal bool SameBudget
    {
        get; set;
    }
    internal bool SameBinding
    {
        get; set;
    }
    internal bool SameLedger
    {
        get; set;
    }
    internal NativeRequestWorkLease Lease => (NativeRequestWorkLease)Binding;
}
