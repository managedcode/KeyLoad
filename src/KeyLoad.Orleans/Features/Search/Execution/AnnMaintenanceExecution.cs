using KeyLoad.Core;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Orleans;

internal static class AnnMaintenanceExecution
{
    internal static async Task<AnnMaintenanceResult> ExecuteAsync(DecodedGrainRequest parent, Func<string, CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> executeChild,
        IServiceProvider services, GrainRequestCodec codec, TimeProvider clock,
        Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
        IOptions<GrainRoutingOptions> routing, ILogger diagnostics,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer)
    {
        var request = GrainNativePayload.ReadCommand<AnnMaintenanceRequest>(parent.Payload);
        var database = services.GetRequiredService<DatabaseEngine>();
        var principal = GrainRequestAuthority.ReloadForRequest(database, parent.Envelope, clock);
        GrainRequestAuthority.RequireAdministrator(principal);
        if (request.CommandId != parent.Envelope.CommandId || request.CommandId == Guid.Empty
            || request.NodeId != database.Store.Identity.NodeId || !Enum.IsDefined(request.Mode))
        { throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest); }
        var children = new AnnMaintenanceChildCalls(parent, executeChild, services, codec, clock, serializer, routing, diagnostics);
        AnnMaintenanceResult result;
        try
        {
            result = await AnnMaintenanceParentFlow.RunAsync(children, request, writer,
                services.GetRequiredService<IOptions<AnnMaintenanceOptions>>().Value, routing.Value).ConfigureAwait(true);
        }
        catch (Exception primary)
        {
            try
            { await children.AbortAsync().ConfigureAwait(true); }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
        await children.AbortAsync().ConfigureAwait(true);
        return result;
    }
}
