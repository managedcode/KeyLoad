using KeyLoad.Clustering;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueryHostedSearchRead
{
    internal static async Task<GrainOperationReply> ExecuteAsync(RequestCqrsHostedRuntimeFixture node,
        QueryHostedSearchDataset data, Guid requestId, CancellationToken token)
    {
        var signed = node.Codec.CreateRead(requestId, data.Principal.Id, GrainReadKind.DistributedSearch,
            OrleansSerialization.Serialize(data.Request));
        var local = node.Services.GetRequiredService<ILocalSiloDetails>();
        using var identity = new GrainRequestIdentityScope(node.Services, data.Principal, requestId,
            Guid.Empty, token, placement: local.SiloAddress, connectionId: node.ConnectionId);
        var connection = node.Grains.GetGrain<IConnectionGrain>(node.ConnectionId);
        return await GrainRequestStreamConsumer.DrainWithPurposeAsync(
            execution => connection.ExecuteStreamAsync(signed, execution),
            node.Services.GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>(),
            requestId, node.Clock, node.RoutingOptions,
            new CqrsStreamPurpose(() => node.Codec.VerifyRequest(signed, requestId)), token).ConfigureAwait(false);
    }

    internal static DistributedSearchPage Page(GrainOperationReply reply)
    {
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, reply.SafeDetail ?? "The execution search returned an error.");
        }
        return OrleansSerialization.Deserialize<GrainValue>(reply.Payload.Span).Value as DistributedSearchPage
            ?? throw new InvalidOperationException("The execution search returned no page.");
    }
}
