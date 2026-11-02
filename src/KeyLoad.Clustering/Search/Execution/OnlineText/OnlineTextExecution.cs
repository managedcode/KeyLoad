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
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: BCL null validation accepts the binding IResourceLifetimeWorkBinding reference through its object parameter. */;
        var request = (GrainPayload.ReadCommand<OnlineTextIndexMaintenanceRequest>((parent.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as KeyLoad.OnlineTextIndexMaintenanceRequest at this closed operation boundary before executing typed code. */);
        var children = new OnlineTextChildCalls(parent, parentEnvelope, executeChild, abort,
            services, codec, diagnostics, context, binding);
        return await MaintenanceChildDispatch.RunThenAbortAsync(() => OnlineTextParentFlow.RunAsync(children, request,
            writer), children.AbortAsync).ConfigureAwait(true);
    }
}
