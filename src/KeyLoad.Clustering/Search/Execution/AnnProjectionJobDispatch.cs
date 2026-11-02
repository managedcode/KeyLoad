using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Clustering;

internal sealed class AnnProjectionJobDispatch(GrainRequestCodec codec, IServiceProvider services)
{
    private const string InvalidResult = "The execution ANN maintenance result is invalid.";

    internal async Task<AnnMaintenanceResult> ExecuteAsync(global::Orleans.IGrainFactory grains,
        PrincipalRecord principal, AnnMaintenanceRequest original, IResourceLifetimeWorkBinding binding,
        CancellationToken token)
    {
        var requestId = Guid.NewGuid();
        var connectionId = ConnectionExecutionIdentity.Resolve(services);
        var failures = new List<Exception>();
        AnnMaintenanceResult? result = null;
        try
        {
            if (binding is not IRequestWorkBinding execution
                || !(ReferenceEquals(binding.ResultRetention, execution.OriginalProducer.ResultRetention) /* KLD0041: Require ANN dispatch to retain the original producer retention owner instance through CLR reference identity. */))
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, AnnProjectionJobProtocol.Invalid);
            }
            execution.OriginalProducer.AdmitPublication();
            var envelope = codec.Issue(GrainRequestEnvelopeConstruction.Command(requestId, codec.Incarnation,
                principal.Id, OperationKind.MaintainAnnIndex, original.CommandId, GrainPayload.Strict(original)));
            using var identity = new GrainRequestIdentityScope(services, principal, requestId,
                original.CommandId, token, connectionId: connectionId);
            try
            {
                result = await ReadAsync(grains, envelope, requestId, original, connectionId, token).ConfigureAwait(true);
            }
            catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
            catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        AnnProjectionJobFailures.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, InvalidResult);
    }

    private async Task<AnnMaintenanceResult> ReadAsync(global::Orleans.IGrainFactory grains, GrainRequestEnvelope envelope,
        Guid requestId, AnnMaintenanceRequest original, Guid connectionId, CancellationToken token)
    {
        var actor = grains.GetGrain<IConnectionGrain>(connectionId);
        var purpose = new CqrsStreamPurpose(() => codec.Decode(envelope));
        var reply = await GrainRequestStreamConsumer.DrainWithPurposeAsync(
            streamToken => actor.ExecuteStreamAsync(envelope, streamToken), requestId, purpose, token).ConfigureAwait(true);
        return Read(reply, original);
    }

    private static AnnMaintenanceResult Read(GrainOperationReply reply, AnnMaintenanceRequest original)
    {
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, reply.SafeDetail ?? InvalidResult);
        }
        var /* KLD0041: Inspect the current generated reply slot before accepting the concrete ANN maintenance response and command identity. */ value = (GrainReplyFactory.RequireValue(reply).Value /* KLD0041: Read the current generated GrainValue field only to validate the concrete RPC response or its permitted absence. */);
        if ((value is not AnnMaintenanceResult result /* KLD0042: Validate the current generated GrainValue slot as KeyLoad.AnnMaintenanceResult before accepting this concrete RPC response. */) || result.CommandId != original.CommandId
            || result.Consumer != original.Consumer || result.IndexGeneration != original.IndexGeneration
            || result.Phase != AnnMaintenancePhase.Completed)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidResult);
        }
        return result;
    }
}
