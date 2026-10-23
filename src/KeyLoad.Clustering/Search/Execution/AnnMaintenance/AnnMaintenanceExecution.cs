using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal static class AnnMaintenanceExecution
{
    internal static async Task<AnnMaintenanceResult> ExecuteAsync(DecodedGrainRequest parent,
        Func<GrainRequestEnvelope, CancellationToken, Task<GrainOperationReply>> executeChild, Func<Guid, Task> abort,
        IServiceProvider services, GrainRequestCodec codec,
        IOptions<GrainRoutingOptions> routing, ILogger diagnostics,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer, CqrsStreamPurpose purpose)
    {
        var request = GrainPayload.ReadCommand<AnnMaintenanceRequest>(parent.Payload);
        purpose.EnableAnnMaintenance(request.Mode);
        var children = new AnnMaintenanceChildCalls(parent, executeChild, abort, services, codec, diagnostics, purpose.WorkBinding);
        AnnMaintenanceResult result;
        try
        {
            result = await AnnMaintenanceParentFlow.RunAsync(children, request, writer,
                services.GetRequiredService<IOptions<AnnMaintenanceOptions>>().Value, routing.Value).ConfigureAwait(true);
        }
        catch (Exception primary)
        {
            try
            {
                await children.AbortAsync().ConfigureAwait(true);
            }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
        await children.AbortAsync().ConfigureAwait(true);
        return result;
    }
}
