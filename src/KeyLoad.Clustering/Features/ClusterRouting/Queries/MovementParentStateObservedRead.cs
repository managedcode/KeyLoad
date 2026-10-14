using KeyLoad.Core;

namespace KeyLoad.Clustering;

internal static class MovementParentStateObservedRead
{
    internal static async Task<object?> ExecuteAsync(DatabaseEngine database, IServiceProvider services,
        TimeProvider clock, PrincipalRecord principal, DecodedGrainRequest request, GrainRequestCodec codec,
        IGrainContext context, CancellationToken cancellationToken)
    {
        await codec.ObservePhaseAsync(request, GrainRequestPhase.MovementParentStateExecuting,
            context, cancellationToken).ConfigureAwait(true);
        var result = GrainControlledReadCapabilities.Execute(database, services, clock,
            principal, request, cancellationToken);
        await codec.ObservePhaseAsync(request, GrainRequestPhase.MovementParentStateReturned,
            context, cancellationToken).ConfigureAwait(true);
        return result;
    }
}
