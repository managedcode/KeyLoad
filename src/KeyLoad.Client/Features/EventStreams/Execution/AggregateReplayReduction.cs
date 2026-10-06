using Microsoft.Extensions.Options;

namespace KeyLoad.Client;

/// <summary>Validates and reduces one complete snapshot-and-tail slice without performing side effects.</summary>
public static class AggregateReplayReduction
{
    /// <summary>Reduces a validated bounded replay slice into state JSON.</summary>
    /// <param name="page">The complete server-authorized snapshot and tail.</param>
    /// <param name="reducer">The exact state and event schema reducer.</param>
    /// <param name="limitsOptions">The bound and validated worker budgets.</param>
    /// <param name="cancellationToken">Token checked before validation and each callback.</param>
    /// <returns>The final valid state JSON.</returns>
    public static string Reduce(
        AggregateReplayPage page,
        AggregateReplayReducer reducer,
        IOptions<AggregateReplayWorkerLimits> limitsOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(limitsOptions);
        var limits = limitsOptions.Value;
        cancellationToken.ThrowIfCancellationRequested();
        AggregateReplayValidation.ValidateLimits(limits);
        AggregateReplayValidation.ValidateReducer(reducer);
        var initialState = AggregateReplayValidation.ValidatePage(page, reducer, limits, cancellationToken);
        var state = initialState;
        foreach (var record in page.Events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            state = reducer.Apply(state, record);
            AggregateReplayJson.ValidateState(state, limits, AggregateReplayMessages.ReducerState);
        }
        return state;
    }
}
