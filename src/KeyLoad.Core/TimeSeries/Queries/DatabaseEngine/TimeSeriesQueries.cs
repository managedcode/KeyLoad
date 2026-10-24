using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Core;
/// <summary>Executes TimeSeriesQueries operations against the caller's database context.</summary>
public static class TimeSeriesQueries
{
    internal static SampleChunkWindowResult ReadSampleChunkWindowBound(this DatabaseEngine engine, string principalId, ReadSampleChunkWindowRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        return engine.ReadSampleChunkWindowRead(principalId, request, budget);
    }
}
