using KeyLoad.Core.Features.Messaging;
using KeyLoad.Orleans;
using KeyLoad.UnitTests.Features.ClusterRouting;
using Microsoft.Extensions.DependencyInjection;
using Orleans.DurableJobs;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class NativeSagaTimeoutJobHarness(
    IGrainFactory grains,
    IServiceProvider services,
    NativeRuntimeTestOptions profile)
{
    private const int NoNewShardClaims = 0;
    private const string NativeJobsDidNotSettle = "Native job shards retained scheduled jobs beyond the test bound.";

    internal async Task<DurableJob> ScheduleAsync(DueWorkHint hint, DateTimeOffset dueTime,
        CancellationToken cancellationToken)
    {
        var target = grains.GetGrain<IRecurringDueCoordinatorGrain>(hint.Lane.Partition.AtomicPartitionId).GetGrainId();
        var request = NativeSagaTimeoutJobContract.CreateScheduleRequest(target, dueTime, hint);
        using var deadline = CreateDeadline(cancellationToken);
        return await services.GetRequiredService<ILocalDurableJobManager>()
            .ScheduleJobAsync(request, deadline.Token);
    }

    internal async Task<List<IJobShard>> CaptureOwnedShardsAsync(DateTimeOffset maxDueTime,
        CancellationToken cancellationToken)
    {
        using var deadline = CreateDeadline(cancellationToken);
        return await services.GetRequiredService<JobShardManager>()
            .AssignJobShardsAsync(maxDueTime, NoNewShardClaims, deadline.Token);
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
        using var deadline = CreateDeadline(cancellationToken);
        try
        {
            while (true)
            {
                if (await CountScheduledJobsAsync(shards, deadline.Token) == 0)
                {
                    return;
                }
                await Task.Delay(profile.PollInterval, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(NativeJobsDidNotSettle);
        }
    }

    private CancellationTokenSource CreateDeadline(CancellationToken cancellationToken)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(profile.CompletionTimeout);
        return deadline;
    }
}
