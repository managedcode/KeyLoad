using KeyLoad.ComparisonHost.Deployment;
using KeyLoad.ComparisonHost.Measurement;

namespace KeyLoad.ComparisonHost.Queues;

/// <summary>The queue case on the job's one live database.</summary>
[BenchmarkDeployment]
[NotInParallel]
internal sealed class QueueBenchmarks(BenchmarkDeployment deployment)
{
    [Test]
    public Task Cycle() => deployment.RunAsync(BenchmarkTypes.Queue, QueueScenario.Cycle, QueueDrivers.Create,
        (Func<IQueueDriver, BenchmarkSettings, CancellationToken, Task<CaseResult>>)QueueScenario.CycleAsync);
}
