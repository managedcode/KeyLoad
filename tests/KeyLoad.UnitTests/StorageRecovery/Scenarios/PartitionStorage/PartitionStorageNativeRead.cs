using KeyLoad.Clustering;
using KeyLoad.UnitTests.Features.ClusterRouting;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using P = KeyLoad.UnitTests.Features.StorageRecovery.PartitionStorageNativeProtocol;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStorageNativeRead
{
    internal static async Task RequireAsync(RequestCqrsHostedRuntimeFixture source,
        CommitToken minimum, CancellationToken token)
    {
        var principal = source.Database.Store.Read(view => source.Database.Database.Principal(view, P.Root, source.Clock.GetUtcNow()));
        var requestId = Guid.NewGuid();
        var signed = source.Codec.CreateRead(requestId, principal.Id, GrainReadKind.Document,
            NativeSerialization.Serialize(new GetDocumentRequest(P.Reference(), minimum)));
        using var scope = new GrainRequestIdentityScope(source.Services, principal, requestId, Guid.Empty,
            token, connectionId: source.ConnectionId);
        var connection = source.Grains.GetGrain<IConnectionGrain>(source.ConnectionId);
        var reply = await GrainRequestStreamConsumer.DrainAsync(
            execution => connection.ExecuteStreamAsync(signed, execution),
            source.Services.GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>(),
            requestId, source.Clock, source.RoutingOptions, token).ConfigureAwait(false);
        await Assert.That(reply.Error).IsNull();
        await Assert.That(reply.SafeDetail).IsNull();
        var actual = NativeSerialization.Deserialize<GrainValue>(reply.Payload.Span).Value as DocumentResult;
        var expected = new DocumentResult(P.Reference(), P.InitialRevision, P.Json, false, []);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected)))
            .IsTrue();
    }
}
