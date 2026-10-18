using KeyLoad.Core.Features.ResourceExecution;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal static class TextMaintenanceExecution
{
    internal static async Task<TextIndexMaintenanceResult> ExecuteAsync(DecodedGrainRequest parent,
        Func<string, CancellationToken, Task<GrainOperationReply>> executeChild, Func<Guid, Task> abort,
        IServiceProvider services, GrainRequestCodec codec,
        IOptions<GrainRoutingOptions> routing, ILogger diagnostics,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer, NativeCqrsStreamPurpose purpose, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var request = GrainNativePayload.ReadCommand<TextIndexMaintenanceRequest>(parent.Payload);
        purpose.EnableTextMaintenance(request.Mode);
        var children = new TextMaintenanceChildCalls(parent, executeChild, abort, services, codec, diagnostics, binding);
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
