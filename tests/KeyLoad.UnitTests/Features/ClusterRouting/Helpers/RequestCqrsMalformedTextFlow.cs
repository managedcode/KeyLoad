using System.Runtime.CompilerServices;
using KeyLoad.Orleans;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsMalformedTextFlow
{
    internal static async Task RunAsync(RequestCqrsClusterFixture fixture)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var principal = fixture.Database.Store.Read(view => fixture.Database.Database.Principal(view,
            KeyLoad.UnitTests.Features.Search.NativeTextMaintenanceTestValues.Principal, fixture.Clock.GetUtcNow()));
        var connection = fixture.Cluster.Client.GetGrain<IConnectionGrain>(fixture.ConnectionId);
        var cut = fixture.Database.Store.Position;
        var bytes = KeyLoad.UnitTests.Features.Messaging.QueueWholeFlowStorage.Bytes(fixture.Database.Store);
        var requestId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var signed = fixture.Codec.CreateCommand(requestId, principal.Id, OperationKind.MaintainTextIndex,
            commandId, NativeSerialization.Serialize(false));
        var frames = new List<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>();
        using (var identity = new GrainRequestIdentityScope(fixture.Cluster.ServiceProvider, principal,
            requestId, commandId, token, connectionId: fixture.ConnectionId))
        {
            var reply = await GrainRequestStreamConsumer.DrainWithPurposeAsync(
                execution => ObserveAsync(connection.ExecuteStreamAsync(signed, execution), frames, execution),
                fixture.Cluster.ServiceProvider.GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>(),
                requestId, fixture.Clock, fixture.RoutingOptions,
                new NativeCqrsStreamPurpose(() => fixture.Codec.VerifyRequest(signed, requestId)), token);
            await Assert.That(reply.Error).IsEqualTo(ErrorCode.Validation);
            await Assert.That(reply.Payload.IsEmpty).IsTrue();
            await Assert.That(reply.SafeDetail).IsEqualTo(GrainRoutingProtocol.InvalidRequest);
        }
        await Assert.That(frames.Select(x => x.Kind).SequenceEqual(new[] { CqrsStreamChunkKind.Started, CqrsStreamChunkKind.Failed })).IsTrue();
        await AssertMalformedShapeAsync(frames, requestId, fixture);
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(cut);
        await Assert.That(KeyLoad.UnitTests.Features.Messaging.QueueWholeFlowStorage.Bytes(fixture.Database.Store)
            .AsSpan().SequenceEqual(bytes)).IsTrue();
        await RequestCqrsActualTextMaintenanceFlow.RunAsync(fixture, TextIndexMaintenanceMode.Build, token);
    }
    private static async Task AssertMalformedShapeAsync(
        List<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> frames,
        Guid requestId, RequestCqrsClusterFixture fixture)
    {
        const int StartedOffset = 0;
        const int FailedOffset = 1;
        var started = frames[StartedOffset].ProgressResult!.Value.Value!;
        await Assert.That(started.RequestId).IsEqualTo(requestId);
        await Assert.That(started.TextPhase).IsNull();
        var final = frames[FailedOffset].Final!.Value;
        await Assert.That(final.IsSuccess).IsFalse();
        await Assert.That(final.Value).IsNull();
        await Assert.That(final.TryGetProblem(out var problem)).IsTrue();
        await Assert.That(GrainRequestStreamProblem.ReadCode(problem!, fixture.RoutingOptions)).IsEqualTo(ErrorCode.Validation);
        await Assert.That(problem!.Detail).IsEqualTo(GrainRoutingProtocol.InvalidRequest);
    }

    private static async IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> ObserveAsync(
        IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> original,
        List<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> frames,
        [EnumeratorCancellation] CancellationToken token)
    {
        await foreach (var chunk in original.WithCancellation(token))
        { frames.Add(chunk); yield return chunk; }
    }

}
