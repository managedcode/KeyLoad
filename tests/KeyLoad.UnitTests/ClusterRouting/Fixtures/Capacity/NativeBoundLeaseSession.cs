using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeBoundLeaseSession
{
    internal NativeRequestWorkOwner? Owner
    {
        get; private set;
    }
    internal ResourceLifetimeWorkResultRetention? Ledger
    {
        get; private set;
    }
    internal NativeRequestWorkLease? Parent
    {
        get; private set;
    }
    internal CancellationTokenSource? Linked
    {
        get; private set;
    }

    internal NativeBoundLeaseOperation Initialize(NativeBoundLeaseSeeded state, CancellationToken token)
    {
        var services = state.Leader.PhysicalServices;
        var database = services.GetRequiredService<DatabaseEngine>();
        Owner = services.GetRequiredService<PartitionStorageScope>().Work;
        Ledger = new(database.OperationLimitsOptions);
        Parent = Owner.Acquire(Guid.NewGuid(), NativeRequestWorkKind.RequestProducer);
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
