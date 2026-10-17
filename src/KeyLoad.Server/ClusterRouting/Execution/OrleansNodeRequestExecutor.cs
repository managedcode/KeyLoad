using KeyLoad.Clustering;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Server;

/// <summary>Bounds one admitted native request, including its cohort check and complete CQRS stream.</summary>
internal sealed class OrleansNodeRequestExecutor(IOptions<GrainRoutingOptions> routingOptions,
    ILogger<OrleansNode> logger)
{
    internal static Task CloseAsync(IGrainFactory grains, IServiceProvider services,
        Guid connectionId, CancellationToken cancellationToken)
        => grains.GetGrain<IConnectionGrain>(connectionId).CloseAsync(
            services.GetRequiredService<GrainRequestCodec>().CreateConnectionClose(connectionId), cancellationToken);

    internal Task<GrainOperationReply> ExecuteAsync(IGrainFactory? factory, IServiceProvider? runtime,
        PhysicalShardCatalogStartup? catalog, bool databaseReady, bool requireCatalogAdmission,
        Guid requestId, string signedRequest, bool command, CancellationToken cancellationToken)
        => ExecuteCoreAsync(factory, runtime, catalog, databaseReady, requireCatalogAdmission,
            requestId, signedRequest, command, null, cancellationToken);

    internal Task<GrainOperationReply> ExecuteAsync(NativeNodeRequestTarget target, bool requireCatalogAdmission,
        Guid requestId, string signedRequest, bool command, CancellationToken cancellationToken)
        => ExecuteAsync(target.Factory, target.Runtime, target.Catalog, target.DatabaseReady,
            requireCatalogAdmission, requestId, signedRequest, command, cancellationToken);

    internal Task<GrainOperationReply> ExecuteWithProgressAsync(NativeNodeRequestTarget target,
        Guid requestId, string signedRequest, Func<GrainRequestProgress, CancellationToken, ValueTask> progress,
        CancellationToken cancellationToken)
        => ExecuteCoreAsync(target.Factory, target.Runtime, target.Catalog, target.DatabaseReady, true,
            requestId, signedRequest, false, progress, cancellationToken);

    private async Task<GrainOperationReply> ExecuteCoreAsync(IGrainFactory? factory, IServiceProvider? runtime,
        PhysicalShardCatalogStartup? catalog, bool databaseReady, bool requireCatalogAdmission,
        Guid requestId, string signedRequest, bool command,
        Func<GrainRequestProgress, CancellationToken, ValueTask>? progress, CancellationToken cancellationToken)
    {
        if (requireCatalogAdmission && (!databaseReady || catalog is null || !catalog.IsReady))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalShardCatalogFence.NotReady);
        }
        var grains = factory ?? throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RoutingUnavailable);
        var services = runtime ?? throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RoutingUnavailable);
        var connectionId = NativeConnectionExecutionIdentity.Resolve(services);
        var clock = services.GetRequiredService<TimeProvider>();
        using var deadline = new CancellationTokenSource(routingOptions.Value.ExecutionLifetime, clock);
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        if (requireCatalogAdmission && catalog is not null)
        {
            if (!catalog.IsReady)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalShardCatalogFence.NotReady);
            }
            await catalog.EnsureAdmissionAsync(execution.Token).ConfigureAwait(false);
        }
        var reply = await DrainAsync(grains, services, connectionId, requestId, signedRequest, command, clock,
            progress, execution.Token, cancellationToken).ConfigureAwait(false);
        if (reply.Error is { } error && reply.Metadata is null)
        {
            throw Errors.Fail(error, reply.SafeDetail ?? OrleansNodeProtocol.ReplyRejected);
        }
        return reply;
    }

    private async Task<GrainOperationReply> DrainAsync(IGrainFactory grains, IServiceProvider services,
        Guid connectionId, Guid requestId, string signedRequest, bool command, TimeProvider clock,
        Func<GrainRequestProgress, CancellationToken, ValueTask>? progress,
        CancellationToken executionToken, CancellationToken callerToken)
    {
        var stage = OrleansRpcFailureStage.Preparation;
        OrleansRpcFailureOperation? originalOperation = null;
        try
        {
            Func<CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> source =
                token => grains.GetGrain<IConnectionGrain>(connectionId).ExecuteStreamAsync(signedRequest, token);
            var serializer = services.GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>();
            var purpose = new NativeCqrsStreamPurpose(() =>
            {
                var request = services.GetRequiredService<GrainRequestCodec>().VerifyRequest(signedRequest, requestId);
                originalOperation = new(request.Envelope.CommandId, request.Envelope.ReadKind, request.Envelope.CommandKind);
                return request;
            });
            stage = OrleansRpcFailureStage.Consumption;
            var reply = progress is null
                ? await GrainRequestStreamConsumer.DrainWithPurposeAsync(source, serializer, requestId, clock,
                    routingOptions, purpose, executionToken).ConfigureAwait(false)
                : await GrainRequestStreamConsumer.DrainWithProgressAsync(source, serializer, requestId, clock,
                    routingOptions, purpose, progress, executionToken).ConfigureAwait(false);
            if (reply.Metadata is not null)
            {
                if (purpose.MetadataAdmission is not { } metadata)
                {
                    throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
                }
                NativeCommandMetadataReplies.Require(reply, metadata, routingOptions);
            }
            return reply;
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            throw OrleansRpcFailure.Translate(failure, command, requestId, logger, stage, originalOperation, callerToken);
        }
    }
}
