using System.Runtime.ExceptionServices;

namespace KeyLoad.Clustering;

/// <summary>Runs the optional producer-disposed observation before execution idle deactivation.</summary>
internal sealed class GrainRequestPhaseSettlement(GrainRequestCodec codec)
{
    private GrainRequestProbeIdentity? identity;
    private Guid connectionId;

    internal void SetIdentity(DecodedGrainRequest request)
    {
        identity = GrainRequestProbeIdentity.From(request.Envelope);
        if (global::Orleans.Runtime.RequestContext.Get(GrainRequestStreamProtocol.ContextKey) is GrainRequestContextState context
            && context.RequestId == request.Envelope.RequestId && context.CommandId == request.Envelope.CommandId)
        {
            connectionId = context.ConnectionId;
        }
    }

    internal async ValueTask SettleAsync(IGrainContext? context)
    {
        var observed = identity;
        identity = null;
        var connection = connectionId;
        connectionId = Guid.Empty;
        if (observed is { } value)
        {
            await codec.ObserveProducerDisposedAsync(value, connection, context).ConfigureAwait(true);
        }
    }

    internal void Settle(IGrainContext? context, Action? deactivate = null)
    {
        var observedIdentity = identity;
        identity = null;
        var probeFailure = observedIdentity is { } value
            ? (Capture(() => codec.ObserveProducerDisposed(value, context))).Value : null;
        var deactivationFailure = deactivate is null ? null : (Capture(deactivate)).Value;
        ThrowCombined(probeFailure, deactivationFailure);
    }

    private static (bool HasValue, Exception? Value) Capture(Action action)
    {
        try
        {
            action();
            return (false, default(Exception?));
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            return (true, ((Exception?)(error)));
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            return (true, ((Exception?)(error)));
        }
    }

    private static void ThrowCombined(Exception? probeFailure, Exception? deactivationFailure)
    {
        if (probeFailure is null)
        {
            Rethrow(deactivationFailure);
            return;
        }

        if (deactivationFailure is null || ReferenceEquals(probeFailure, deactivationFailure))
        {
            Rethrow(probeFailure);
            return;
        }

        ExceptionDispatchInfo.Capture(new AggregateException(probeFailure, deactivationFailure)).Throw();
    }

    private static void Rethrow(Exception? error)
    {
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
