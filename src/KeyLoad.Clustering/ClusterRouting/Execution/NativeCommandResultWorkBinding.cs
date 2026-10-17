using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

/// <summary>Binds the actual reply's footprint under a fresh cut before publishing user-visible data.</summary>
internal static class NativeCommandResultWorkBinding
{
    private const string Uncertain = "The write outcome is unknown. Retry the same command ID.";
    private const string OriginalFailureKey = "keyload.native-command-work-binding-failure";

    internal static async Task BindBeforePublicationAsync(IServiceProvider services,
        TimeProvider clock, DecodedGrainRequest request, GrainOperationReply reply,
        ReadExecutionBudget work, IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        if (reply.Error is not null)
        {
            return;
        }
        try
        {
            var proof = RequireProof(request, reply);
            await BindCurrentAsync(services, clock, request, proof, proof.Lifetimes,
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

    internal static async Task BindObservedBeforeEncodingAsync(IServiceProvider services,
        TimeProvider clock, DecodedGrainRequest request, GrainOperationReply first,
        NativeCommandObservationResultV1 observed, ReadExecutionBudget work,
        IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        var proof = RequireProof(request, first);
        work.CheckResult(proof);
        var lifetimes = proof.Lifetimes.Union(observed.Lifetimes).ToImmutableArray();
        await BindCurrentAsync(services, clock, request, proof, lifetimes,
            work, binding, token).ConfigureAwait(true);
        work.CheckResult(observed.Result.NativeValue);
        work.Check();
    }

    private static async Task BindCurrentAsync(IServiceProvider services, TimeProvider clock,
        DecodedGrainRequest request, NativeCommandWorkProofV1 proof,
        ImmutableArray<ResourceLifetimeV1> lifetimes, ReadExecutionBudget work,
        IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        var database = services.GetRequiredService<DatabaseEngine>();
        work.RequireResourceLifetimeBinding(binding);
        work.CheckResult(proof);
        await services.GetRequiredService<Func<ICommitCoordinator>>()().ReadBarrierAsync(token).ConfigureAwait(true);
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
