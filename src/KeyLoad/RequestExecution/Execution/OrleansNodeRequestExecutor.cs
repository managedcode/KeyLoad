using KeyLoad.Clustering;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Server;

/// <summary>Bounds one admitted execution request and its complete CQRS response stream.</summary>
internal sealed class OrleansNodeRequestExecutor(ILogger<OrleansNode> logger)
{
    internal static Task CloseAsync(IGrainFactory grains, IServiceProvider services,
        Guid connectionId, CancellationToken cancellationToken)
        => grains.GetGrain<IConnectionGrain>(connectionId).CloseAsync(
            services.GetRequiredService<GrainRequestCodec>().CreateConnectionClose(connectionId), cancellationToken);

    internal Task<GrainOperationReply> ExecuteAsync(IGrainFactory? factory, IServiceProvider? runtime,
        bool databaseReady, bool requireDatabaseReady,
        Guid requestId, GrainRequestEnvelope envelope, bool command, CancellationToken cancellationToken)
        => ExecuteCoreAsync(factory, runtime, databaseReady, requireDatabaseReady,
            requestId, envelope, command, null, cancellationToken);

    internal Task<GrainOperationReply> ExecuteAsync(NodeRequestTarget target, bool requireDatabaseReady,
        Guid requestId, GrainRequestEnvelope envelope, bool command, CancellationToken cancellationToken)
        => ExecuteAsync(target.Factory, target.Runtime, target.DatabaseReady,
            requireDatabaseReady, requestId, envelope, command, cancellationToken);

    internal Task<GrainOperationReply> ExecuteWithProgressAsync(NodeRequestTarget target,
        Guid requestId, GrainRequestEnvelope envelope, Func<GrainRequestProgress, CancellationToken, ValueTask> progress,
        CancellationToken cancellationToken)
        => ExecuteCoreAsync(target.Factory, target.Runtime, target.DatabaseReady, true,
            requestId, envelope, false, progress, cancellationToken);

    private async Task<GrainOperationReply> ExecuteCoreAsync(IGrainFactory? factory, IServiceProvider? runtime,
        bool databaseReady, bool requireDatabaseReady,
        Guid requestId, GrainRequestEnvelope envelope, bool command,
        Func<GrainRequestProgress, CancellationToken, ValueTask>? progress, CancellationToken cancellationToken)
    {
        if (requireDatabaseReady && !databaseReady)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RoutingUnavailable);
        }
        var grains = factory ?? throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RoutingUnavailable);
        var services = runtime ?? throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RoutingUnavailable);
        var connectionId = ConnectionExecutionIdentity.Resolve(services);
        var reply = await DrainAsync(grains, services, connectionId, requestId, envelope, command,
            progress, cancellationToken);
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, reply.SafeDetail ?? OrleansNodeProtocol.ReplyRejected);
        }
        return reply;
    }

    private async Task<GrainOperationReply> DrainAsync(IGrainFactory grains, IServiceProvider services,
        Guid connectionId, Guid requestId, GrainRequestEnvelope envelope, bool command,
        Func<GrainRequestProgress, CancellationToken, ValueTask>? progress, CancellationToken executionToken)
    {
        var stage = OrleansRpcFailureStage.Preparation;
        OrleansRpcFailureOperation? originalOperation = null;
        try
        {
            var purpose = new CqrsStreamPurpose(() =>
            {
                var request = services.GetRequiredService<GrainRequestCodec>().DecodeRequest(envelope, requestId);
                originalOperation = new(request.Envelope.CommandId, request.Envelope.ReadKind, request.Envelope.CommandKind);
                return request;
            });
            stage = OrleansRpcFailureStage.Consumption;
            if (progress is null && !ConnectionParentExecution.Handles(envelope.CommandKind))
            {
                purpose.Started();
                return await grains.GetGrain<IConnectionGrain>(connectionId).ExecuteAsync(envelope, executionToken);
            }
            Func<CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> source =
                token => grains.GetGrain<IConnectionGrain>(connectionId).ExecuteStreamAsync(envelope, token);
            return progress is null
                ? await GrainRequestStreamConsumer.DrainWithPurposeAsync(source, requestId, purpose, executionToken)
                : await GrainRequestStreamConsumer.DrainWithProgressAsync(source, requestId, purpose, progress, executionToken);
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
        {
            throw OrleansRpcFailure.Translate(failure, command, requestId, logger, stage, originalOperation, executionToken);
        }
    }
}
