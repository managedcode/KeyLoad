using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Clustering;

/// <summary>Keeps actual command admission, original budget and returned-outcome publication in one boundary.</summary>
internal static class ConnectionCommandExecution
{
    internal static async Task<GrainOperationReply> ExecuteAsync(DecodedGrainRequest request, GrainRequestEnvelope envelope,
        SiloDatabaseClient storage, SiloAddress owner, GrainRequestCodec codec, TimeProvider clock,
        IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        var stage = GrainFailureStage.PartitionResolution;
        try
        {
            // Execution expiry remains an admission check before any new command invocation.
            codec.ValidateScope(request.Envelope);
            GrainIdentityContext.Validate(request.Envelope, request.Envelope.RequestId);
            var partition = GrainPartitionResolver.Resolve(request);
            stage = GrainFailureStage.CapabilityExecution;
            var work = new ReadExecutionBudget(clock, token);
            work.ConstrainLifetime(request.Envelope.ExpiresAt);
            work.AttachResourceLifetimeBinding(binding);
            var reply = await storage.ExecuteAsync(owner, envelope, token, partition).ConfigureAwait(true);
            CommandResultWorkBinding.BindReturned(request, reply, work, binding);
            return reply;
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
        {
            GrainFailureDiagnostics.Mark(failure, stage);
            throw;
        }
    }
}
