using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.EventStreams;

/// <summary>Borrows the original Event work owner through one joined, native reserved command child.</summary>
internal static class EventVectorNativeChild
{
    private const int NoFailures = 0;

    internal static async Task<GrainOperationReply> ExecuteAsync(OrleansNode node, PrincipalRecord principal,
        Guid requestId, Guid commandId, string signed, ReadExecutionBudget work, RemoteModelNativeEffectBoundary? boundary, CancellationToken token)
    {
        var failures = new List<Exception>();
        var cleanup = new List<Exception>();
        NativeRequestWorkChildReservation? reservation = null;
        CancellationTokenSource? cancellation = null;
        GrainRequestIdentityScope? identity = null;
        GrainOperationReply? reply = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var binding = work.RequireAttachedResourceLifetimeBinding();
            if (binding is not INativeRequestWorkBinding native
                || !ReferenceEquals(binding.ResultRetention, native.OriginalProducer.ResultRetention))
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, EventVectorRemoteProtocol.Invalid);
            }
            var verified = node.CatalogRequestCodec().VerifyRequest(signed, requestId);
            if (verified.Envelope.PrincipalId != principal.Id || verified.Envelope.CommandId != commandId
                || verified.Envelope.Purpose is not (EventVectorRequestScope.ControlPurpose or EventVectorRequestScope.SourcePurpose))
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, EventVectorRemoteProtocol.Invalid);
            }
            reservation = native.OriginalProducer.ReserveLocalChild(verified,
                node.RuntimeServices.GetRequiredService<PhysicalShardRecord>(),
                node.RuntimeServices.GetRequiredService<SiloStorageIdentityProvider>().Read());
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, reservation.Cancellation);
            identity = node.OpenRequestContext(principal, requestId, commandId, cancellation.Token);
            boundary?.Begin(cancellation.Token);
            reply = await node.ExecuteAsync(requestId, signed, command: true,
                cancellationToken: cancellation.Token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        var original = failures.Count == NoFailures ? null : failures[NoFailures];
        ServerFailureObserver.Observe(() => identity?.Dispose(), cleanup);
        ServerFailureObserver.Observe(() => cancellation?.Dispose(), cleanup);
        failures.AddRange(cleanup);
        if (reservation is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var report = await reservation.CompleteDispatchAsync(original, [.. cleanup]).ConfigureAwait(false);
                if (report.CleanupOrFatalFailure is { } failure)
                {
                    throw failure;
                }
            }, failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return reply ?? throw Errors.Fail(ErrorCode.Corruption, EventVectorRemoteProtocol.Invalid);
    }
}
