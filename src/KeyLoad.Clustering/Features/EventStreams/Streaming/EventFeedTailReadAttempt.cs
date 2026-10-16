using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

internal sealed class EventFeedTailReadAttempt(IEventVectorParent actualParent,
    DecodedGrainRequest original, ReadExecutionBudget work, IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
{
    internal EventFeedControlResult? Value
    {
        get; private set;
    }

    internal async Task ReadAsync()
    {
        Value = await actualParent.ReadTailOfferAsync(original, work, binding, cancellationToken).ConfigureAwait(true);
    }
}
