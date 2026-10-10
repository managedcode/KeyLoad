using KeyLoad.Core;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Orleans;

internal static class OnlineTextExecution
{
    private const long UnallocatedConsumerGeneration = 0;
    internal static async Task<OnlineTextIndexMaintenanceResult> ExecuteAsync(DecodedGrainRequest parent,
        Func<string, CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> executeChild, IServiceProvider services, GrainRequestCodec codec, TimeProvider clock,
        Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
        IOptions<GrainRoutingOptions> routing, ILogger diagnostics, global::Orleans.Runtime.IGrainContext context,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer)
    {
        var request = GrainNativePayload.ReadCommand<OnlineTextIndexMaintenanceRequest>(parent.Payload);
        var database = services.GetRequiredService<DatabaseEngine>();
        var principal = GrainRequestAuthority.ReloadForRequest(database, parent.Envelope, clock);
        GrainRequestAuthority.RequireAdministrator(principal);
        if (request.CommandId != parent.Envelope.CommandId || request.CommandId == Guid.Empty
            || request.NodeId != database.Store.Identity.NodeId || request.ConsumerGeneration <= UnallocatedConsumerGeneration)
        { throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest); }
        var frames = new OnlineTextFrameAccounting(routing);
        var children = new OnlineTextChildCalls(parent, executeChild, services, codec, clock, serializer, routing, diagnostics, frames, context);
        OnlineTextIndexMaintenanceResult result;
        try
        {
            result = await OnlineTextParentFlow.RunAsync(children, request, frames, writer,
                services.GetRequiredService<IOptions<TextIndexMaintenanceOptions>>().Value).ConfigureAwait(true);
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
