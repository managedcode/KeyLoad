using KeyLoad.Orleans;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Server;

/// <summary>Bounds one admitted native request, including its cohort check and complete CQRS stream.</summary>
internal sealed class OrleansNodeRequestExecutor(IOptions<GrainRoutingOptions> routingOptions,
    ILogger<OrleansNode> logger)
{
    internal async Task<GrainOperationReply> ExecuteAsync(IGrainFactory grains, IServiceProvider services,
        PhysicalShardCatalogStartup? catalog, Guid requestId, string signedRequest, bool command,
        CancellationToken cancellationToken)
    {
        var clock = services.GetRequiredService<TimeProvider>();
        using var deadline = new CancellationTokenSource(routingOptions.Value.ExecutionLifetime, clock);
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        await services.GetRequiredService<ReplicaSiloDiscoveryClient>()
            .EnsureCompatibleCohortAsync(execution.Token).ConfigureAwait(false);
        if (catalog is not null)
        {
            if (!catalog.IsReady)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalShardCatalogFence.NotReady);
            }
            await catalog.EnsureAdmissionAsync(execution.Token).ConfigureAwait(false);
        }
        var reply = await DrainAsync(grains, services, requestId, signedRequest, command, clock,
            execution.Token, cancellationToken).ConfigureAwait(false);
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, reply.SafeDetail ?? OrleansNodeProtocol.ReplyRejected);
        }
        return reply;
    }

    private async Task<GrainOperationReply> DrainAsync(IGrainFactory grains, IServiceProvider services,
        Guid requestId, string signedRequest, bool command, TimeProvider clock,
        CancellationToken executionToken, CancellationToken callerToken)
    {
        try
        {
            return await GrainRequestStreamConsumer.DrainAsync(
                createStream: token => grains.GetGrain<IRequestGrain>(requestId).ExecuteStreamAsync(signedRequest, token),
                serializer: services.GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>(),
                requestId: requestId, clock: clock, cancellationToken: executionToken, options: routingOptions).ConfigureAwait(false);
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            throw OrleansRpcFailure.Translate(failure, command, requestId, logger, callerToken);
        }
    }
}
