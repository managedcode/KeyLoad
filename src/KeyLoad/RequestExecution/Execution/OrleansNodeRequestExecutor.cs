using KeyLoad.Clustering;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Server;

/// <summary>Bounds one admitted execution request and its complete CQRS response stream.</summary>
internal sealed class OrleansNodeRequestExecutor(ILogger<OrleansNode> logger)
{
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
        GrainOperationReply reply;
        var stage = OrleansRpcFailureStage.Preparation;
        OrleansRpcFailureOperation? originalOperation = null;
        try
        {
            var actor = grains.GetGrain<IRequestGrain>(RequestExecutionGrainIdentity.For(requestId));
            stage = OrleansRpcFailureStage.Consumption;
            if (progress is null && !ConnectionParentExecution.Handles(envelope.CommandKind))
            {
                _ = VerifyOriginal();
                reply = await actor.ExecuteAsync(envelope, cancellationToken);
            }
            else
            {
                reply = await DrainStreamAsync(actor, envelope, new CqrsStreamPurpose(VerifyOriginal), progress, cancellationToken);
            }
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
        {
            throw OrleansRpcFailure.Translate(failure, command, requestId, logger, stage, originalOperation, cancellationToken);
        }
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, reply.SafeDetail ?? OrleansNodeProtocol.ReplyRejected);
        }
        return reply;

        DecodedGrainRequest VerifyOriginal()
        {
            var request = services.GetRequiredService<GrainRequestCodec>().DecodeRequest(envelope, requestId);
            originalOperation = new(request.Envelope.CommandId, request.Envelope.ReadKind, request.Envelope.CommandKind);
            return request;
        }
    }

    private static async Task<GrainOperationReply> DrainStreamAsync(IRequestGrain actor, GrainRequestEnvelope envelope,
        CqrsStreamPurpose purpose, Func<GrainRequestProgress, CancellationToken, ValueTask>? progress, CancellationToken token)
    {
        GrainOperationReply? reply = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            Func<CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> source =
                execution => actor.ExecuteStreamAsync(envelope, execution);
            reply = progress is null
                ? await GrainRequestStreamConsumer.DrainWithPurposeAsync(source, envelope.RequestId, purpose, token)
                : await GrainRequestStreamConsumer.DrainWithProgressAsync(source, envelope.RequestId, purpose, progress, token);
        }, failures);
        await ServerFailureObserver.ObserveAsync(actor.ReleaseAsync, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return reply ?? throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.ReplyRejected);
    }
}
