using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.BenchmarkScenarios.BenchmarkComparisons;

/// <summary>Holds the validated execution snapshots borrowed by an embedded benchmark owner.</summary>
internal sealed record EmbeddedBenchmarkRuntimeOptions(IOptions<DatabaseLimits> Database,
    IOptions<DueWorkExecutionOptions> DueWork, IOptions<EventSourceExecutionOptions> EventSource,
    IOptions<MessagingExecutionOptions> Messaging, IOptions<GraphExecutionOptions> GraphExecution,
    IOptions<ChangeFeedExecutionOptions> ChangeFeedExecution,
    IOptions<ClaimsExecutionOptions> RuntimeClaimsExecution,
    IOptions<TimeSeriesExecutionOptions> TimeSeriesExecution,
    IOptions<ZoneTreeStorageExecutionOptions> Storage,
    IOptions<ZoneTreePointCacheExecutionOptions> PointCache);
