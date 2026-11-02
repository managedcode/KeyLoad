using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Core;
/// <summary>Executes TimeSeriesQueries operations against the caller's database context.</summary>
public static class TimeSeriesQueries
{
    internal static SampleChunkWindowResult ReadSampleChunkWindowBound(this DatabaseEngine engine, string principalId, ReadSampleChunkWindowRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(budget) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        budget.AttachResourceLifetimeBinding(binding);
        return engine.ReadSampleChunkWindowRead(principalId, request, budget);
    }
}
