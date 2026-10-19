using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Retains bounded original derived generations inside the existing physical host lifetime.</summary>
internal sealed class UserAnalyticsRuntime(IOptions<DatabaseLimits> options) : INativeUserAnalytics, IAsyncDisposable
{
    private const int NoCleanupFailures = 0;
    private const string Unavailable = "The user analytics physical owner is unavailable.";
    private const string Busy = "The user analytics generation allowance is exhausted.";
    private readonly Lock lifecycle = new();
    private readonly List<Work> operations = [];
    private PartitionHost? host;
    private Task? closing;
    private bool stopping;

    internal bool CanReleasePhysical
    {
        get
        {
            lock (lifecycle)
            {
                return operations.All(work => work.Original.IsCompleted && !work.Execution.HasUnjoinedEngine);
            }
        }
    }

    internal void Open(PartitionHost physical)
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(stopping, this);
            if (host is not null)
            {
                throw new InvalidOperationException(Unavailable);
            }
            options.Value.Validate();
            UserAnalyticsGenerationPaths.PrepareRoot(physical);
            host = physical;
        }
    }

    public Task<NativeAnalyticsAggregate> AggregateAsync(AnalyticsAggregateSnapshot snapshot,
        IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.Work.RequireResourceLifetimeBinding(binding);
        cancellationToken.ThrowIfCancellationRequested();
        UserAnalyticsGenerationExecution execution;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(stopping, this);
            var physical = host ?? throw new InvalidOperationException(Unavailable);
            operations.RemoveAll(work => work.Original.IsCompleted && !work.Execution.HasUnjoinedEngine
                && work.Execution.CleanupFailures.Count == NoCleanupFailures);
            if (operations.Any(work => work.Original.IsCompleted && work.Execution.HasUnjoinedEngine))
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, Unavailable);
            }
            if (operations.Count >= options.Value.MaxConcurrentQueries)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, Busy);
            }
            execution = new(physical);
            operations.Add(new(execution, completion.Task));
        }
        try
        {
            return Task.FromResult(execution.Execute(snapshot, binding, cancellationToken));
        }
        finally
        {
            completion.SetResult();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            stopping = true;
            return new(closing ??= CloseAsync(operations.ToArray()));
        }
    }

    private async Task CloseAsync(Work[] originals)
    {
        await Task.Yield();
        var failures = new List<Exception>();
        foreach (var work in originals)
        {
            // The connection owns its query outcome; this owner separately retains every native cleanup failure.
            await work.Original.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            failures.AddRange(work.Execution.CleanupFailures);
        }
        if (!CanReleasePhysical)
        {
            failures.Add(Errors.Fail(ErrorCode.OwnershipLost, Unavailable));
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private sealed record Work(UserAnalyticsGenerationExecution Execution, Task Original);
}
