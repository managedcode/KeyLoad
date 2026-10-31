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
        ArgumentNullException.ThrowIfNull(binding);
        var request = GrainPayload.ReadCommand<TextIndexMaintenanceRequest>(parent.Payload);
        purpose.EnableTextMaintenance(request.Mode);
        var children = new MaintenanceChildCalls(parent, executeChild, abort, services, codec, diagnostics, binding,
            TextMaintenanceChildScope.Purpose, TextIndexMaintenanceProtocol.Interrupted);
        return await children.RunAsync(() => TextMaintenanceParentFlow.RunAsync(children, request, writer))
            .ConfigureAwait(true);
    }
}
