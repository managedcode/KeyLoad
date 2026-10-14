using KeyLoad.Core;

namespace KeyLoad.Clustering;

internal interface IPlacementIntentExecutor
{
    Task<PlacementIntentV1> ApplyAsync(GrainRequestEnvelope trusted, ApplyPlacementIntentRequest request,
        ReadExecutionBudget work, Func<PlacementIntentV1, ValueTask> progress, CancellationToken token);
}
