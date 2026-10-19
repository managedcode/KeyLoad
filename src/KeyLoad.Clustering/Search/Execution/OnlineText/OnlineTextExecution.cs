using KeyLoad.Core.Features.ResourceExecution;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal static class OnlineTextExecution
{
    internal static async Task<OnlineTextIndexMaintenanceResult> ExecuteAsync(DecodedGrainRequest parent, string signedParent,
        Func<string, CancellationToken, Task<GrainOperationReply>> executeChild, Func<Guid, Task> abort,
        IServiceProvider services, GrainRequestCodec codec,
        IOptions<GrainRoutingOptions> routing, ILogger diagnostics, global::Orleans.Runtime.IGrainContext context,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var request = GrainPayload.ReadCommand<OnlineTextIndexMaintenanceRequest>(parent.Payload);
        var frames = new OnlineTextFrameAccounting(routing);
        var children = new OnlineTextChildCalls(parent, signedParent, executeChild, abort,
            services, codec, diagnostics, frames, context, binding);
        OnlineTextIndexMaintenanceResult result;
        try
        {
            result = await OnlineTextParentFlow.RunAsync(children, request, frames, writer,
                services.GetRequiredService<IOptions<TextIndexMaintenanceOptions>>().Value).ConfigureAwait(true);
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
