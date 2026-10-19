using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

/// <summary>Owns one storage maintenance session budget and ledger independently of its temporary request stages.</summary>
internal sealed class NativeMaintenanceSessionWork : IDisposable, IResourceLifetimeWorkBinding
{
    private readonly CancellationTokenSource deadline;
    private readonly CancellationTokenSource lifetime;
    private readonly ResourceLifetimeWorkResultRetention retention;
    private readonly Lock gate = new();
    private Stage? current;
    private bool closed;
    private bool releaseAfterStage;
    private bool disposed;
    internal DateTimeOffset OriginalExpiry { get; }
    internal ReadExecutionBudget Budget { get; }
    internal bool ReleaseRequested => releaseAfterStage || disposed;
    public ResourceLifetimeWorkResultRetention ResultRetention
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return retention;
        }
    }

    internal NativeMaintenanceSessionWork(IOptions<DatabaseLimits> limits, TimeProvider clock,
        DateTimeOffset originalExpiry, CancellationToken shutdown)
    {
        OriginalExpiry = originalExpiry;
        var remaining = originalExpiry - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            throw NativeTextErrors.Ownership();
        }
        retention = new(limits);
        var bound = TimeSpan.FromSeconds(limits.Value.QueryDeadlineSeconds);
        deadline = new(remaining < bound ? remaining : bound, clock);
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(shutdown, deadline.Token);
        try
        {
            Budget = new(limits, clock, lifetime.Token);
            Budget.ConstrainLifetime(originalExpiry);
            Budget.AttachResourceLifetimeBinding(this);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(retention.Dispose, failures);
            ServerFailureObserver.Observe(lifetime.Dispose, failures);
            ServerFailureObserver.Observe(deadline.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal void RequireExpiry(DateTimeOffset? actual)
    {
        Budget.Check();
        if (actual is { } expiry && expiry != OriginalExpiry)
        {
            throw NativeTextErrors.Ownership();
        }
    }

    internal IDisposable EnterStage(IResourceLifetimeWorkBinding? binding, CancellationToken token)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed || closed, this);
            if (current is not null)
            {
                throw NativeTextErrors.Ownership();
            }
            binding?.Bind(ImmutableArray<ResourceLifetimeV1>.Empty);
            var cancellation = Budget.EnterStageCancellation(token);
            var stage = new Stage(this, binding, cancellation);
            Volatile.Write(ref current, stage);
            return stage;
        }
    }

    internal async Task CancelAndJoinStagesAsync()
    {
        Task joined;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            closed = true;
            joined = current?.Joined ?? Task.CompletedTask;
        }
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(lifetime.CancelAsync, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => joined, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal void DisposeAfterStage()
    {
        lock (gate)
        {
            closed = true;
            if (current is not null)
            {
                releaseAfterStage = true;
                return;
            }
            Dispose();
        }
    }

    public void Bind(ResourceLifetimeCoverageV1 coverage) => Bind(coverage.Lifetimes);
    public void Bind(ImmutableArray<ResourceLifetimeV1> lifetimes)
    {
        Budget.Check();
        ResultRetention.Reserve(lifetimes);
        Volatile.Read(ref current)?.Binding?.Bind(lifetimes);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (current is not null)
            {
                throw NativeTextErrors.Ownership();
            }
            if (disposed)
            {
                return;
            }
            retention.Dispose();
            lifetime.Dispose();
            deadline.Dispose();
            disposed = true;
            closed = true;
        }
    }

    private void CompleteStage(Stage original)
    {
        lock (gate)
        {
            if (!ReferenceEquals(current, original))
            {
                throw NativeTextErrors.Ownership();
            }
            Volatile.Write(ref current, null);
            if (releaseAfterStage)
            {
                Dispose();
            }
        }
    }

    private sealed class Stage(NativeMaintenanceSessionWork owner, IResourceLifetimeWorkBinding? binding,
        ReadExecutionBudgetStageCancellationLease cancellation) : IDisposable
    {
        private readonly TaskCompletionSource joined = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool disposed;
        internal Task Joined => joined.Task;
        internal IResourceLifetimeWorkBinding? Binding { get; } = binding;
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            try
            {
                cancellation.Dispose();
                owner.CompleteStage(this);
                disposed = true;
            }
            finally
            {
                joined.TrySetResult();
            }
        }
    }
}
