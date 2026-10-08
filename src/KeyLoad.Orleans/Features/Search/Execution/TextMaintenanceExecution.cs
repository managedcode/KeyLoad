using KeyLoad.Core;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Orleans;

internal static class TextMaintenanceExecution
{
    internal static async Task<TextIndexMaintenanceResult> ExecuteAsync(DecodedGrainRequest parent, IGrainFactory grains,
        IServiceProvider services, GrainRequestCodec codec, TimeProvider clock,
        Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
        IOptions<GrainRoutingOptions> routing, ILogger diagnostics,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer)
    {
        var request = GrainNativePayload.ReadCommand<TextIndexMaintenanceRequest>(parent.Payload);
        var database = services.GetRequiredService<DatabaseEngine>();
        var principal = GrainRequestAuthority.ReloadForRequest(database, parent.Envelope, clock);
        GrainRequestAuthority.RequireAdministrator(principal);
        if (request.CommandId != parent.Envelope.CommandId || request.CommandId == Guid.Empty
            || request.NodeId != database.Store.Identity.NodeId || !Enum.IsDefined(request.Mode))
        { throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest); }
        var children = new TextMaintenanceChildCalls(parent, grains, services, codec, clock, serializer, routing, diagnostics);
        TextIndexMaintenanceResult result;
        try
        {
            result = await TextMaintenanceParentFlow.RunAsync(children, request, writer,
                services.GetRequiredService<IOptions<TextIndexMaintenanceOptions>>().Value, routing.Value).ConfigureAwait(true);
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
