using KeyLoad.Clustering;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class BoundLeaseSession
{
    internal RequestWorkOwner? Owner
    {
        get; private set;
    }
    internal ResourceLifetimeWorkResultRetention? Ledger
    {
        get; private set;
    }
    internal RequestWorkLease? Parent
    {
        get; private set;
    }
    internal CancellationTokenSource? Linked
    {
        get; private set;
    }

    internal BoundLeaseOperation Initialize(BoundLeaseSeeded state, CancellationToken token)
    {
        var services = state.Leader.PhysicalServices;

        Owner = services.GetRequiredService<PartitionStorageScope>().Work;
        Ledger = new();
        Parent = Owner.Acquire(Guid.NewGuid(), RequestWorkKind.RequestProducer);
        Parent.AttachResultRetention(Ledger);
        Linked = CancellationTokenSource.CreateLinkedTokenSource(token, Parent.Cancellation);
        var budget = new ReadExecutionBudget(Linked.Token);
        budget.AttachResourceLifetimeBinding(Parent);
        return new(Owner, Ledger, Parent, budget, Linked.Token);
    }
}
