using KeyLoad.Core.ResourceExecution;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Logging;

namespace KeyLoad.Clustering;

internal static class OnlineTextExecution
{
    internal static async Task<OnlineTextIndexMaintenanceResult> ExecuteAsync(DecodedGrainRequest parent, GrainRequestEnvelope parentEnvelope,
        Func<GrainRequestEnvelope, CancellationToken, Task<GrainOperationReply>> executeChild, Func<Guid, Task> abort,
        IServiceProvider services, GrainRequestCodec codec,
        ILogger diagnostics, global::Orleans.Runtime.IGrainContext context,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var request = GrainPayload.ReadCommand<OnlineTextIndexMaintenanceRequest>(parent.Payload);
        var children = new OnlineTextChildCalls(parent, parentEnvelope, executeChild, abort,
            services, codec, diagnostics, context, binding);
        return await MaintenanceChildDispatch.RunThenAbortAsync(() => OnlineTextParentFlow.RunAsync(children, request,
            writer), children.AbortAsync).ConfigureAwait(true);
    }
}
