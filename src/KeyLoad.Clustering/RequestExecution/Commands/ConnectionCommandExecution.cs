using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Keeps actual command admission, original budget and returned-outcome publication in one boundary.</summary>
internal static class ConnectionCommandExecution
{
    internal static async Task<GrainOperationReply> ExecuteAsync(DecodedGrainRequest request, string signed,
        global::Orleans.IGrainFactory grains, IServiceProvider services, GrainRequestCodec codec, TimeProvider clock,
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
            var work = new ReadExecutionBudget(services.GetRequiredService<IOptions<DatabaseLimits>>(), clock, token);
            work.ConstrainLifetime(request.Envelope.ExpiresAt);
            work.AttachResourceLifetimeBinding(binding);
            var target = grains.GetGrain<ICommandPartitionGrain>(partition);
            var reply = await target.ExecuteAsync(signed, token).ConfigureAwait(true);
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
