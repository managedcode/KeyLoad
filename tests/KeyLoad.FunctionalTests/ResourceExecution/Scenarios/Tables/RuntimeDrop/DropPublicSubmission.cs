using KeyLoad.Clustering;
using KeyLoad.Server;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.FunctionalTests.ResourceExecution;

internal static class DropPublicSubmission
{
    internal static DropPublicCall Start(DropRuntimeProfile profile, DropTableRequest body,
        Guid commandId, CancellationToken token)
    {
        var node = profile.Node;
        var principal = profile.Source.Store.Read(view => profile.Source.Database.Principal(view,
            PartitionStoreProtocol.AdministratorId, node.Clock.GetUtcNow()));
        var requestId = Guid.NewGuid();
        var envelope = node.Codec.CreateCommand(requestId, principal.Id, OperationKind.DropTable, commandId,
            body);
        var original = ExecuteAsync(node, principal, requestId, commandId, envelope, token);
        return new(requestId, commandId, profile.Track(original));
    }

    internal static Task<GrainOperationReply> SubmitAsync(DropRuntimeProfile profile, OperationKind kind,
        Guid commandId, string principalId, object payload, CancellationToken token)
    {
        var node = profile.Node;
        var principal = profile.Source.Store.Read(view => profile.Source.Database.Principal(view,
            principalId, node.Clock.GetUtcNow()));
        var requestId = Guid.NewGuid();
        var envelope = node.Codec.CreateCommand(requestId, principal.Id, kind, commandId, payload);
        return profile.Track(ExecuteAsync(node, principal, requestId, commandId, envelope, token));
    }

    private static async Task<GrainOperationReply> ExecuteAsync(RequestCqrsHostedRuntimeFixture node,
        PrincipalRecord principal, Guid requestId, Guid commandId, GrainRequestEnvelope envelope, CancellationToken token)
    {
        var failures = new List<Exception>();
        GrainOperationReply? reply = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var identity = new GrainRequestIdentityScope(node.Services, principal,
                requestId, commandId, token, connectionId: node.ConnectionId);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                reply = await GrainRequestStreamConsumer.DrainAsync(
                    execution => node.Grains.GetGrain<IConnectionGrain>(node.ConnectionId).ExecuteStreamAsync(envelope, execution),
                    node.Services.GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>(),
                    requestId, node.Clock, node.RoutingOptions, token).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return reply ?? throw Errors.Fail(ErrorCode.RecoveryRequired, DropRuntimeProtocol.MissingReply);
    }
}
