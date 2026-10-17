using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Clustering;

internal static class TextMaintenanceExecution
{
    internal static async Task<TextIndexMaintenanceResult> ExecuteAsync(DecodedGrainRequest parent, Func<string, CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> executeChild,
        IServiceProvider services, GrainRequestCodec codec, TimeProvider clock,
        Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
        IOptions<GrainRoutingOptions> routing, ILogger diagnostics,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer, NativeCqrsStreamPurpose purpose, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var request = GrainNativePayload.ReadCommand<TextIndexMaintenanceRequest>(parent.Payload);
        var database = services.GetRequiredService<DatabaseEngine>();
        var principal = GrainRequestAuthority.ReloadForRequest(database, parent.Envelope, clock);
        GrainRequestAuthority.RequireAdministrator(principal);
        if (request.CommandId != parent.Envelope.CommandId || request.CommandId == Guid.Empty
            || request.NodeId != database.Store.Identity.NodeId || !Enum.IsDefined(request.Mode))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        purpose.EnableTextMaintenance(request.Mode);
        var children = new TextMaintenanceChildCalls(parent, executeChild, services, codec, clock, serializer, routing, diagnostics, binding);
        TextIndexMaintenanceResult result;
        try
        {
            result = await TextMaintenanceParentFlow.RunAsync(children, request, writer,
                services.GetRequiredService<IOptions<TextIndexMaintenanceOptions>>().Value, routing.Value).ConfigureAwait(true);
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
