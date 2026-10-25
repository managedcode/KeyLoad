using KeyLoad.Clustering;
using KeyLoad.Core.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Orleans.DurableJobs;

namespace KeyLoad.FunctionalTests.Messaging;

internal sealed class SagaTimeoutJobHarness(
    IGrainFactory grains,
    IServiceProvider services,
    RuntimeTestOptions profile)
{
    private const string JobsDidNotSettle = "Execution job shards retained scheduled jobs beyond the test bound.";

    internal async Task<DurableJob> ScheduleAsync(DueWorkHint hint, DateTimeOffset dueTime,
        CancellationToken cancellationToken)
    {
        var target = grains.GetGrain<IRecurringDueCoordinatorGrain>(hint.Lane.Partition.AtomicPartitionId).GetGrainId();
        var request = SagaTimeoutJobContract.CreateScheduleRequest(target, dueTime, hint);
        using var timeout = new CancellationTokenSource(profile.CompletionTimeout, services.GetRequiredService<TimeProvider>());
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        return await services.GetRequiredService<ILocalDurableJobManager>()
            .ScheduleJobAsync(request, deadline.Token);
    }

    internal async Task<List<IJobShard>> CaptureOwnedShardsAsync(DateTimeOffset maxDueTime,
        CancellationToken cancellationToken)
        => await AssignJobShardsAsync(maxDueTime, 0, cancellationToken);

    internal async Task<List<IJobShard>> RecoverShardsAsync(DateTimeOffset maxDueTime,
        CancellationToken cancellationToken)
        => await AssignJobShardsAsync(maxDueTime,
            services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RuntimeDurableJobOptions>>()
                .Value.MaximumClaimBudget, cancellationToken);

    private async Task<List<IJobShard>> AssignJobShardsAsync(DateTimeOffset maxDueTime, int maximumNewClaims,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(profile.CompletionTimeout, services.GetRequiredService<TimeProvider>());
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        return await services.GetRequiredService<JobShardManager>()
            .AssignJobShardsAsync(maxDueTime, maximumNewClaims, deadline.Token);
    }

    internal static async Task<int> CountScheduledJobsAsync(IEnumerable<IJobShard> shards,
        CancellationToken cancellationToken = default)
    {
        var count = 0;
        foreach (var shard in shards)
        {
            cancellationToken.ThrowIfCancellationRequested();
            count += await shard.GetJobCountAsync();
        }
        return count;
    }

    internal static IReadOnlyCollection<IJobShard> MergeShards(IEnumerable<IJobShard> first,
        IEnumerable<IJobShard> second)
        => first.Concat(second).DistinctBy(shard => shard.Id).ToArray();

    internal async Task WaitUntilSettledAsync(IReadOnlyCollection<IJobShard> shards,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(profile.CompletionTimeout, services.GetRequiredService<TimeProvider>());
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            while (true)
            {
                if (await CountScheduledJobsAsync(shards, deadline.Token) == 0)
                {
                    return;
                }
                await Task.Delay(profile.PollInterval, TimeProvider.System, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(JobsDidNotSettle);
        }
    }

}
