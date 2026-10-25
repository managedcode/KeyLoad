using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Core.StoragePartitions.Contracts;

namespace KeyLoad.Server.Search;

/// <summary>Owns one storage maintenance session budget and ledger independently of its temporary request stages.</summary>
internal sealed class MaintenanceSessionWork : IDisposable, IResourceLifetimeWorkBinding
{
    private readonly CancellationTokenSource deadline;
    private readonly CancellationTokenSource lifetime;
    private readonly ResourceLifetimeWorkResultRetention retention;
    private readonly Lock gate = new();
    private Stage? current;
    private bool closed;
    private bool releaseAfterStage;
    private bool disposed;
    /// <summary>Gets the caller's explicit expiry, or <see cref="DateTimeOffset.MaxValue"/> when only cancellation ends the session.</summary>
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

    internal MaintenanceSessionWork(TimeProvider clock,
        DateTimeOffset? originalExpiry, CancellationToken shutdown)
    {
        OriginalExpiry = originalExpiry ?? DateTimeOffset.MaxValue;
        var remaining = originalExpiry is { } expiry ? expiry - clock.GetUtcNow() : Timeout.InfiniteTimeSpan;
        if (originalExpiry is not null && remaining <= TimeSpan.Zero)
        {
            throw TextErrors.Ownership();
        }
        retention = new();
        deadline = new(remaining, clock);
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(shutdown, deadline.Token);
        try
        {
            Budget = new(clock, lifetime.Token);
            if (originalExpiry is { } explicitExpiry)
            {
                Budget.ConstrainLifetime(explicitExpiry);
            }
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
            throw TextErrors.Ownership();
        }
    }

    internal IDisposable EnterStage(IResourceLifetimeWorkBinding? binding, CancellationToken token)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed || closed, this);
            if (current is not null)
            {
                throw TextErrors.Ownership();
            }
            binding?.Bind(ImmutableArray<ResourceLifetime>.Empty);
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
        await ServerFailureObserver.ObserveAsync(lifetime.CancelAsync, failures);
        await ServerFailureObserver.ObserveAsync(() => joined, failures);
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

    public void Bind(ResourceLifetimeCoverage coverage) => Bind(coverage.Lifetimes);
    public void Bind(ImmutableArray<ResourceLifetime> lifetimes)
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
                throw TextErrors.Ownership();
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
                throw TextErrors.Ownership();
            }
            Volatile.Write(ref current, null);
            if (releaseAfterStage)
            {
                Dispose();
            }
        }
    }

    private sealed class Stage(MaintenanceSessionWork owner, IResourceLifetimeWorkBinding? binding,
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
