using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeLocalChildLifetimeSession
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
    internal List<NativeRequestWorkChildReservation> Reservations { get; } = [];
    internal List<Task> Producers { get; } = [];
    internal TaskCompletionSource ReleaseProducer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource ProducerReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal NativeLocalChildLifetimeOperation Initialize(NativeLocalChildLifetimeSeeded state, CancellationToken token)
    {
        var database = state.Leader.Journal.Database;
        Owner = new(UnitRoutingOptions.Routing());
        Owner.BindPhysicalOwner(state.PhysicalOwner);
        Owner.AttachResourceLifetimeWorkFence(database, state.Leader.Journal.Log.State.LastIndex);
        Ledger = new(database.OperationLimitsOptions);
        Parent = Owner.Acquire(Guid.NewGuid(), NativeRequestWorkKind.RequestProducer);
        Parent.AttachResultRetention(Ledger);
        Linked = CancellationTokenSource.CreateLinkedTokenSource(token, Parent.Cancellation);
        var budget = new ReadExecutionBudget(database.OperationLimitsOptions, database.EvaluationClock, Linked.Token);
        budget.AttachResourceLifetimeBinding(Parent);
        var codec = new GrainRequestCodec(database, database.EvaluationClock, UnitRoutingOptions.Routing());
        var lifetime = codec.ExecutionOptions.Value.RequestLifetime;
        var queryLifetime = TimeSpan.FromSeconds(database.Limits.QueryDeadlineSeconds);
        var expiry = database.EvaluationClock.GetUtcNow() + (lifetime < queryLifetime ? lifetime : queryLifetime);
        budget.ConstrainLifetime(expiry);
        return new(Owner, Ledger, Parent, budget, codec, expiry, Linked.Token);
    }

    internal NativeRequestWorkChildReservation Reserve(NativeLocalChildLifetimeOperation operation,
        NativeLocalChildLifetimeSeeded state, NativeLocalChildLifetimeInput input)
    {
        var reservation = operation.Parent.ReserveLocalChild(input.Verified,
            state.PhysicalOwner, state.Leader.Discovery.Read());
        Reservations.Add(reservation);
        return reservation;
    }
}
