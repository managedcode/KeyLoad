using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

/// <summary>Binds the actual reply's footprint under a fresh cut before publishing user-visible data.</summary>
internal static class NativeCommandResultWorkBinding
{
    private const string Uncertain = "The write outcome is unknown. Retry the same command ID.";
    private const string OriginalFailureKey = "keyload.native-command-work-binding-failure";

    internal static async Task BindBeforePublicationAsync(DatabaseEngine database, ICommitCoordinator coordinator,
        TimeProvider clock, DecodedGrainRequest request, GrainOperationReply reply,
        IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        if (reply.Error is not null)
        {
            return;
        }
        try
        {
            var work = new ReadExecutionBudget(database.OperationLimitsOptions, clock, token);
            work.ConstrainLifetime(request.Envelope.ExpiresAt);
            work.AttachResourceLifetimeBinding(binding);
            var proof = RequireProof(request, reply);
            await BindCurrentAsync(database, coordinator, clock, request, proof, proof.Lifetimes,
                work, binding, token).ConfigureAwait(true);
            work.CheckResult(GrainNativePayload.Read<GrainValue>(reply.Payload).Value);
            work.Check();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            var unknown = Errors.Fail(ErrorCode.UnknownWriteOutcome, Uncertain);
            unknown.Data[OriginalFailureKey] = error;
            throw unknown;
        }
    }

    internal static void BindReturned(DecodedGrainRequest request, GrainOperationReply reply,
        ReadExecutionBudget work, IResourceLifetimeWorkBinding binding)
    {
        if (reply.Error is not null)
        {
            return;
        }
        try
        {
            var proof = RequireProof(request, reply);
            work.CheckResult(proof);
            binding.Bind(proof.Lifetimes);
            work.CheckResult(GrainNativePayload.Read<GrainValue>(reply.Payload).Value);
            work.Check();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            var unknown = Errors.Fail(ErrorCode.UnknownWriteOutcome, Uncertain);
            unknown.Data[OriginalFailureKey] = error;
            throw unknown;
        }
    }

    private static async Task BindCurrentAsync(DatabaseEngine database, ICommitCoordinator coordinator, TimeProvider clock,
        DecodedGrainRequest request, NativeCommandWorkProofV1 proof,
        ImmutableArray<ResourceLifetimeV1> lifetimes, ReadExecutionBudget work,
        IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        work.RequireResourceLifetimeBinding(binding);
        work.CheckResult(proof);
        await coordinator.ReadBarrierAsync(token).ConfigureAwait(true);
        GrainIdentityContext.Validate(request.Envelope, request.Envelope.RequestId);
        var principal = GrainRequestAuthority.ReloadForRequest(database, request.Envelope, clock);
        database.BindAuthenticatedCommandResultLifetimes(principal.Id, proof.PolicyEpoch,
            lifetimes, work, binding);
    }

    internal static NativeCommandWorkProofV1 RequireProof(DecodedGrainRequest request, GrainOperationReply reply)
    {
        var proof = reply.WorkProof;
        if (proof is null || proof.Version != NativeCommandWorkProofV1.CurrentVersion
            || proof.RequestId != request.Envelope.RequestId || proof.CommandId != request.Envelope.CommandId
            || proof.PrincipalId != request.Envelope.PrincipalId || proof.Kind != request.Envelope.CommandKind
            || proof.PolicyEpoch < NativeCommandWorkProofV1.MinimumPolicyEpoch || proof.Lifetimes.IsDefault)
        {
            throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
        }
        return proof;
    }
}
