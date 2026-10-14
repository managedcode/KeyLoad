using KeyLoad.Core;

namespace KeyLoad.Clustering;

internal sealed class EventFeedTailReadAttempt(IEventVectorParent actualParent,
    DecodedGrainRequest original, ReadExecutionBudget work, CancellationToken cancellationToken)
{
    internal EventFeedControlResult? Value
    {
        get; private set;
    }

    internal async Task ReadAsync()
    {
        Value = await actualParent.ReadTailOfferAsync(original, work, cancellationToken).ConfigureAwait(true);
    }
}
