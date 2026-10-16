using KeyLoad.Clustering;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.Search;

internal static class AnnProjectionNativeLifetimeQuery
{
    internal static async Task<AnnProjectionObservationResult> ExecuteAsync(OrleansNode node,
        PartitionHost partition, TimeProvider clock, PrincipalRecord principal,
        LocalAnnObservationFence admitted, AnnProjectionNativeCallV2 call, byte[] payload, ReadExecutionBudget work,
        CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid();
        var signed = node.CatalogRequestCodec().CreateAnnProjectionObservationV2(
            requestId, principal.Id, payload, call.Original.ExpiresAt);
        var failures = new List<Exception>();
        AnnProjectionObservationResult? result = null;
        try
        {
            var connectionId = node.RuntimeServices.GetRequiredService<NativeConnectionOwnerIdentity>().Id;
            using var identity = node.OpenRequestContext(principal, requestId, Guid.Empty, cancellationToken,
                connectionId: connectionId);
            try
            {
                var reply = await node.ExecuteAsync(requestId, signed, command: false,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                var current = partition.Database.Store.Read(view =>
                    partition.Database.Principal(work.CreateView(view), principal.Id, clock.GetUtcNow()));
                if (current.PolicyEpoch != principal.PolicyEpoch)
                {
                    throw Errors.Fail(ErrorCode.TokenInvalidated, AnnProjectionObservationProtocol.Invalid);
                }
                partition.Database.RequireLocalAnnObservation(principal.Id, call.Original.Observation.OriginalRequest, admitted, work,
                    new(call.Definition, call.Coverage, call.Principal, call.Definitions));
                work.ChargeBytes(reply.Payload.Length);
                if (reply.Error is { } error)
                {
                    throw Errors.Fail(error, AnnProjectionObservationProtocol.Invalid);
                }
                result = GrainNativePayload.Read<GrainValue>(reply.Payload).Value as AnnProjectionObservationResult
                    ?? throw Errors.Fail(ErrorCode.Corruption, AnnProjectionObservationProtocol.Invalid);
                if (result.AcceptedReadBytes < AnnProjectionObservationProtocol.NoReadWork
                    || result.AcceptedReadBytes > call.Original.Observation.MaximumReadBytes)
                {
                    throw Errors.Fail(ErrorCode.Unauthenticated, AnnProjectionObservationProtocol.Invalid);
                }
                // Counter import is bounded by the original signed child request, independent of reply shape.
                if (result.ExaminedRecords < AnnProjectionObservationProtocol.NoReadWork
                    || result.ExaminedRecords > call.Original.Observation.MaximumRecords)
                {
                    throw Errors.Fail(ErrorCode.Unauthenticated, AnnProjectionObservationProtocol.Invalid);
                }
                work.ImportNativeObservation(result.AcceptedReadBytes, result.ExaminedRecords);
                work.Check();
            }
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
            catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, AnnProjectionObservationProtocol.Invalid);
    }
}
