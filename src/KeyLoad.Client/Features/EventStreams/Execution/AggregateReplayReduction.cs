using Microsoft.Extensions.Options;

namespace KeyLoad.Client;

/// <summary>Validates and reduces one complete snapshot-and-tail slice without performing side effects.</summary>
public static class AggregateReplayReduction
{
    /// <summary>Reduces a validated bounded replay slice into state JSON.</summary>
    /// <param name="page">The complete server-authorized snapshot and tail.</param>
    /// <param name="reducer">The exact state and event schema reducer.</param>
    /// <param name="upcasters">Optional one-version event payload transforms.</param>
    /// <param name="limitsOptions">The bound and validated worker budgets.</param>
    /// <param name="cancellationToken">Token checked before validation and each callback.</param>
    /// <returns>The final valid state JSON.</returns>
    public static string Reduce(
        AggregateReplayPage page,
        AggregateReplayReducer reducer,
        IOptions<AggregateReplayWorkerLimits> limitsOptions,
        IEnumerable<EventUpcaster>? upcasters = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(limitsOptions);
        var limits = limitsOptions.Value;
        cancellationToken.ThrowIfCancellationRequested();
        AggregateReplayValidation.ValidateLimits(limits);
        AggregateReplayValidation.ValidateReducer(reducer);
        var resolved = AggregateReplayValidation.ValidatePage(page, reducer, upcasters, limits, cancellationToken);
        var state = resolved.InitialState;
        foreach (var record in page.Events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = resolved.Paths[record.Data.SchemaVersion];
            var transformed = AggregateReplayUpcast.Apply(record, path, limits, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            state = reducer.Apply(state, transformed);
            AggregateReplayJson.ValidateState(state, limits, AggregateReplayMessages.ReducerState);
        }
        return state;
    }
}
