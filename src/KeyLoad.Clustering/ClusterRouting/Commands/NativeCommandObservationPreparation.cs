using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Retains the authenticated original intent and owner before any command effect is dispatched.</summary>
internal static class NativeCommandObservationPreparation
{
    internal const string Alias = "keyload.prepare-original-command.v1";

    internal static async Task<GrainOperationReply> ExecuteAsync(DatabaseEngine database,
        ICommitCoordinator coordinator, TimeProvider clock, IOptions<GrainRoutingOptions> options,
        GrainRequestCodec codec, NativeRequestWorkOwner owner, IPhysicalRequestPlacement? physical,
        ReplicaSiloDiscoveryState? runtime, DecodedGrainRequest request, string actorKey,
        IGrainContext? context, CancellationToken token)
    {
        var preparation = default(NativeCommandObservationPreparationWork);
        NativeCommandObservation? observation = null;
        Exception? primary = null;
        try
        {
            token.ThrowIfCancellationRequested();
            if (request.Envelope.Observation is null || !NativeCommandObservationScope.IsEligible(request))
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
            }
            GrainIdentityContext.Validate(request.Envelope, request.Envelope.RequestId);
            PhysicalCommandActorKey.Validate(request, actorKey, physical);
            preparation.Acquire(owner, request.Envelope.RequestId,
                database.OperationLimitsOptions, token);
            var work = preparation.Work;
            observation = await NativeCommandObservationSubmission.ReserveAsync(database, clock, request,
                owner, work, physical, runtime, token).ConfigureAwait(true)
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
            var reply = await PrepareAsync(database, coordinator, clock, options, codec, physical, request,
                context, work, observation).ConfigureAwait(true);
            preparation.TransferTo(observation);
            observation.Budget.Check();
            observation.Lease.AdmitPublication();
            observation.FinishPreparation();
            return reply;
        }
        catch (Exception error)
        {
            preparation.TransferTo(observation);
            observation?.FinishPreparation();
            primary = await NativeCommandObservationSubmission.AbortAsync(observation, error).ConfigureAwait(true);
            NativeRequestWorkSettlement.Rethrow(primary);
            throw;
        }
        finally
        {
            observation?.FinishPreparation();
            preparation.PrimaryError = primary;
            await preparation.DisposeAsync().ConfigureAwait(true);
        }
    }

    private static async Task<GrainOperationReply> PrepareAsync(DatabaseEngine database,
        ICommitCoordinator coordinator, TimeProvider clock, IOptions<GrainRoutingOptions> options,
        GrainRequestCodec codec, IPhysicalRequestPlacement? physical, DecodedGrainRequest request,
        IGrainContext? context, NativeCapabilityWorkLifetime work, NativeCommandObservation observation)
    {
        var principal = await NativeCommandCapabilityAuthorization.AuthorizeAsync(database, coordinator,
            clock, options.Value, codec, physical, request, observation, context,
            observation.Token).ConfigureAwait(true);
        var original = database.CreateNativeOperation(request.Envelope.CommandKind!.Value,
            request.Envelope.CommandId, principal.Id, clock.GetUtcNow(), request.Payload, observation.Budget);
        NativeCommandWorkAdmission.BindObserved(database, request, principal, clock,
            work, original, observation.Budget);
        observation.AttachIntent(original, work);
        var proof = NativeCommandWorkAdmission.Capture(request, work, principal.PolicyEpoch) with
        {
            ObservationToken = observation.SignedToken
        };
        var reply = new GrainOperationReply { WorkProof = proof };
        if (NativeSerialization.Measure(reply) > Math.Min(observation.Budget.MaximumResultBytes,
            options.Value.MaximumReplyBytes))
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, GrainRoutingProtocol.ReplyBudgetExceeded);
        }
        observation.Budget.Check();
        return reply;
    }
}
