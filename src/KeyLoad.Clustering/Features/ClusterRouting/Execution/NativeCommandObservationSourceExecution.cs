using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

/// <summary>Owns one pre-effect source reservation through the same target's strict outcome and joined cleanup.</summary>
internal static class NativeCommandObservationSourceExecution
{
    internal static async Task<GrainOperationReply> ExecuteAsync(DecodedGrainRequest request,
        ICommandPartitionGrain target, IServiceProvider services, GrainRequestCodec codec,
        TimeProvider clock, ReadExecutionBudget work, IResourceLifetimeWorkBinding binding,
        CancellationToken token)
    {
        var transport = services.GetRequiredService<INativeCommandObservationTransport>();
        var scope = NativeCommandObservationSourceScope.Create(request, codec, work);
        Exception? failure = null;
        GrainOperationReply? result = null;
        try
        {
            var prepared = await target.PrepareOriginalCommandAsync(scope.Signed, token).ConfigureAwait(true);
            await codec.ObserveNativePhaseAsync(scope.Request, GrainRequestPhase.OriginalClaimReturned,
                services.GetRequiredService<ReplicaSiloDiscoveryState>(), token).ConfigureAwait(true);
            if (prepared.Error is not null)
            {
                result = prepared;
            }
            else
            {
                var opaque = scope.AcceptClaim(prepared, work);
                scope.Dispatched = true;
                var first = await transport.ExecuteAsync(opaque, token).ConfigureAwait(true);
                await codec.ObserveNativePhaseAsync(scope.Request, GrainRequestPhase.OriginalBusinessReplyReturned,
                    services.GetRequiredService<ReplicaSiloDiscoveryState>(), token).ConfigureAwait(true);
                if (first.Error is { } code)
                {
                    throw Errors.Fail(code, first.SafeDetail ?? GrainRoutingProtocol.InvalidRequest);
                }
                scope.RequireBusinessProof(first, work);
                var reply = await transport.ReadAsync(opaque, token).ConfigureAwait(true);
                var observed = scope.Accept(reply, work);
                scope.TargetSettled = true;
                await NativeCommandResultWorkBinding.BindObservedBeforeEncodingAsync(services, clock,
                    scope.Request, first, observed, work, binding, token).ConfigureAwait(true);
                result = NativeCommandObservationSourceReply.Encode(observed, first, services, work, token);
            }
        }
        catch (Exception error)
        {
            failure = error;
        }
        failure = await scope.CompleteAsync(transport, work, failure).ConfigureAwait(true);
        NativeRequestWorkSettlement.Rethrow(failure);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
    }
}
