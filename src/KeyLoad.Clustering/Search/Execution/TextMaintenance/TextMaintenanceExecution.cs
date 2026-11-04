using KeyLoad.Core.ResourceExecution;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Logging;

namespace KeyLoad.Clustering;

internal static class TextMaintenanceExecution
{
    internal static async Task<TextIndexMaintenanceResult> ExecuteAsync(DecodedGrainRequest parent,
        Func<GrainRequestEnvelope, CancellationToken, Task<GrainOperationReply>> executeChild, Func<Guid, Task> abort,
        IServiceProvider services, GrainRequestCodec codec, ILogger diagnostics,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer, CqrsStreamPurpose purpose, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: BCL null validation accepts the binding IResourceLifetimeWorkBinding reference through its object parameter. */;
        var request = (GrainPayload.ReadCommand<TextIndexMaintenanceRequest>((parent.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as KeyLoad.TextIndexMaintenanceRequest at this closed operation boundary before executing typed code. */);
        purpose.EnableTextMaintenance(request.Mode);
        var children = new MaintenanceChildCalls(parent, executeChild, abort, services, codec, diagnostics, binding,
            TextMaintenanceChildScope.Purpose, TextIndexMaintenanceProtocol.Interrupted);
        return await children.RunAsync(() => TextMaintenanceParentFlow.RunAsync(children, request, writer));
    }
}
