using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Clustering;

/// <summary>Composes native model-parent capabilities while the connection retains its original stream and work owner.</summary>
internal static class NativeConnectionParentExecution
{
    internal static async Task<GrainOperationReply> ExecuteAsync(DecodedGrainRequest request,
        Func<string, CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> executeChild,
        IServiceProvider services, GrainRequestCodec codec, TimeProvider clock,
        Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> chunkSerializer,
        IOptions<GrainRoutingOptions> options, ILogger diagnostics, global::Orleans.Runtime.IGrainContext context,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer, NativeCqrsStreamPurpose purpose)
    {
        if (request.Envelope.Purpose == ResourceRetirementCommandScope.Purpose)
        {
            return await NativeResourceRetirementParentExecution.ExecuteAsync(request, services, clock, codec,
                context, writer, purpose.WorkBinding, purpose.RecordFailure).ConfigureAwait(true);
        }
        if (request.Envelope.Purpose == RemoteGraphRequestScope.ParentPurpose)
        {
            return await RemoteGraphParentExecution.ExecuteAsync(request, services, clock, codec, context, writer, purpose.WorkBinding).ConfigureAwait(true);
        }
        if (request.Envelope.Purpose == EventVectorRequestScope.ParentPurpose)
        {
            return await EventVectorParentExecution.ExecuteAdmitAsync(request, services, clock, codec, context, writer, purpose.WorkBinding).ConfigureAwait(true);
        }
        if (request.Envelope.CommandKind == OperationKind.MaintainOnlineTextIndex)
        {
            var result = await OnlineTextExecution.ExecuteAsync(request, executeChild, services, codec,
                clock, chunkSerializer, options, diagnostics, context, writer, purpose.WorkBinding).ConfigureAwait(true);
            return GrainReplyFactory.ParentValue(result, options, writer.CancellationToken);
        }
        if (request.Envelope.CommandKind == OperationKind.MaintainTextIndex)
        {
            var result = await TextMaintenanceExecution.ExecuteAsync(request, executeChild, services, codec,
                clock, chunkSerializer, options, diagnostics, writer, purpose, purpose.WorkBinding).ConfigureAwait(true);
            return GrainReplyFactory.ParentValue(result, options, writer.CancellationToken);
        }
        if (request.Envelope.CommandKind == OperationKind.MaintainAnnIndex)
        {
            var result = await AnnMaintenanceExecution.ExecuteAsync(request, executeChild, services, codec,
                clock, chunkSerializer, options, diagnostics, writer, purpose).ConfigureAwait(true);
            return GrainReplyFactory.ParentValue(result, options, writer.CancellationToken);
        }
        var lanes = await MultiLaneReceiveExecution.ExecuteAsync(request, executeChild, services, codec,
            clock, chunkSerializer, options, diagnostics, purpose.WorkBinding, writer.CancellationToken).ConfigureAwait(true);
        return GrainReplyFactory.ParentValue(lanes, options, writer.CancellationToken);
    }
}
