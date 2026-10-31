using KeyLoad.Clustering;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class QueryHostedSearchRead
{
    internal static async Task<GrainOperationReply> ExecuteAsync(RequestCqrsHostedRuntimeFixture node,
        QueryHostedSearchDataset data, Guid requestId, CancellationToken token)
    {
        var envelope = node.Codec.CreateRead(requestId, data.Principal.Id, GrainReadKind.DistributedSearch,
            data.Request);
        var local = node.Services.GetRequiredService<ILocalSiloDetails>();
        using var identity = new GrainRequestIdentityScope(node.Services, data.Principal, requestId,
            Guid.Empty, token, placement: local.SiloAddress, connectionId: node.ConnectionId);
        var connection = node.Grains.GetGrain<IConnectionGrain>(node.ConnectionId);
        return await GrainRequestStreamConsumer.DrainWithPurposeAsync(
            execution => connection.ExecuteStreamAsync(envelope, execution),
            requestId,
            new CqrsStreamPurpose(() => node.Codec.DecodeRequest(envelope, requestId)), token).ConfigureAwait(false);
    }

    internal static DistributedSearchPage Page(GrainOperationReply reply)
    {
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, reply.SafeDetail ?? "The execution search returned an error.");
        }
        return GrainReplyFactory.RequireValue(reply).Value as DistributedSearchPage
            ?? throw new InvalidOperationException("The execution search returned no page.");
    }
}
