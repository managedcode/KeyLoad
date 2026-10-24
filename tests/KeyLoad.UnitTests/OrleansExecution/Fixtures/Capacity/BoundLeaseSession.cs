using KeyLoad.Clustering;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.OrleansExecution;

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
        var database = services.GetRequiredService<DatabaseEngine>();
        Owner = services.GetRequiredService<PartitionStorageScope>().Work;
        Ledger = new(database.OperationLimitsOptions);
        Parent = Owner.Acquire(Guid.NewGuid(), RequestWorkKind.RequestProducer);
        Parent.AttachResultRetention(Ledger);
        Linked = CancellationTokenSource.CreateLinkedTokenSource(token, Parent.Cancellation);
        var budget = new ReadExecutionBudget(database.OperationLimitsOptions, database.EvaluationClock, Linked.Token);
        budget.AttachResourceLifetimeBinding(Parent);
        var lifetime = state.Leader.RoutingOptions.Value.RequestLifetime;
        var queryLifetime = TimeSpan.FromSeconds(database.Limits.QueryDeadlineSeconds);
        var expiry = database.EvaluationClock.GetUtcNow() + (lifetime < queryLifetime ? lifetime : queryLifetime);
        budget.ConstrainLifetime(expiry);
        return new(Owner, Ledger, Parent, budget, expiry, Linked.Token);
    }
}
