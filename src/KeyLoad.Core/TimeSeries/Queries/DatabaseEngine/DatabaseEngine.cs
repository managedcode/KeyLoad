using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal SampleChunkWindowResult ReadSampleChunkWindowBound(string principalId,
        ReadSampleChunkWindowRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        return ReadSampleChunkWindowRead(principalId, request, budget);
    }
}
