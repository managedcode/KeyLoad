using KeyLoad.Core;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Composes model phases on the connected grain while storage executes bounded native Task leaves.</summary>
internal static class NativeConnectionParentExecution
{
    private const long UnallocatedConsumerGeneration = 0;

    internal static bool Handles(OperationKind? kind)
        => kind is OperationKind.MaintainOnlineTextIndex or OperationKind.MaintainTextIndex
            or OperationKind.MaintainAnnIndex or OperationKind.ReceiveAcrossLanes;

    internal static void Authorize(DatabaseEngine database, DecodedGrainRequest request, TimeProvider clock)
    {
        var principal = GrainRequestAuthority.ReloadForRequest(database, request.Envelope, clock);
        if (request.Envelope.CommandKind == OperationKind.ReceiveAcrossLanes)
        {
            return;
        }
        GrainRequestAuthority.RequireAdministrator(principal);
        switch (request.Envelope.CommandKind)
        {
            case OperationKind.MaintainAnnIndex:
                var ann = GrainNativePayload.ReadCommand<AnnMaintenanceRequest>(request.Payload);
                RequireTarget(database, request, ann.CommandId, ann.NodeId, Enum.IsDefined(ann.Mode));
                break;
            case OperationKind.MaintainTextIndex:
                var text = GrainNativePayload.ReadCommand<TextIndexMaintenanceRequest>(request.Payload);
                RequireTarget(database, request, text.CommandId, text.NodeId, Enum.IsDefined(text.Mode));
                break;
            case OperationKind.MaintainOnlineTextIndex:
                var online = GrainNativePayload.ReadCommand<OnlineTextIndexMaintenanceRequest>(request.Payload);
                RequireTarget(database, request, online.CommandId, online.NodeId,
                    online.ConsumerGeneration > UnallocatedConsumerGeneration);
                break;
            default:
                throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
    }

    private static void RequireTarget(DatabaseEngine database, DecodedGrainRequest parent,
        Guid commandId, Guid nodeId, bool validMode)
    {
        if (commandId != parent.Envelope.CommandId || commandId == Guid.Empty
            || nodeId != database.Store.Identity.NodeId || !validMode)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
    }

    internal static async Task<GrainOperationReply> ExecuteAsync(DecodedGrainRequest request, string signedParent,
        Func<string, CancellationToken, Task<GrainOperationReply>> executeChild,
        Func<GrainReadKind, Guid, Task> abortMaintenance,
        IServiceProvider services, GrainRequestCodec codec, TimeProvider clock,
        IOptions<GrainRoutingOptions> options, ILogger diagnostics, IGrainContext context,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer, NativeCqrsStreamPurpose purpose)
    {
        if (request.Envelope.CommandKind == OperationKind.MaintainOnlineTextIndex)
        {
            var result = await OnlineTextExecution.ExecuteAsync(request, signedParent, executeChild,
                id => abortMaintenance(GrainReadKind.OnlineTextMaintenance, id), services, codec,
                options, diagnostics, context, writer, purpose.WorkBinding).ConfigureAwait(true);
            return GrainReplyFactory.ParentValue(result, options, writer.CancellationToken);
        }
        if (request.Envelope.CommandKind == OperationKind.MaintainTextIndex)
        {
            var result = await TextMaintenanceExecution.ExecuteAsync(request, executeChild,
                id => abortMaintenance(GrainReadKind.TextMaintenance, id), services, codec,
                options, diagnostics, writer, purpose, purpose.WorkBinding).ConfigureAwait(true);
            return GrainReplyFactory.ParentValue(result, options, writer.CancellationToken);
        }
        if (request.Envelope.CommandKind == OperationKind.MaintainAnnIndex)
        {
            var result = await AnnMaintenanceExecution.ExecuteAsync(request, executeChild,
                id => abortMaintenance(GrainReadKind.AnnMaintenance, id), services, codec,
                options, diagnostics, writer, purpose).ConfigureAwait(true);
            return GrainReplyFactory.ParentValue(result, options, writer.CancellationToken);
        }
        var lanes = await MultiLaneReceiveExecution.ExecuteAsync(request, executeChild, services, codec,
            clock, diagnostics, purpose.WorkBinding, writer.CancellationToken).ConfigureAwait(true);
        return GrainReplyFactory.ParentValue(lanes, options, writer.CancellationToken);
    }
}
