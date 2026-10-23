using System.Runtime.CompilerServices;
using KeyLoad.Clustering;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal static class RequestCqrsAnnInvocation
{
    internal static async Task<(AnnMaintenanceResult Result, GrainOperationReply Reply)> RunAsync(
        RequestCqrsHostedRuntimeFixture fixture, AnnMaintenanceRequest request, CancellationToken token, bool retainedReplay = false)
    {
        var id = Guid.NewGuid();
        var envelope = fixture.Codec.CreateCommand(id, RequestCqrsAnnTestData.Principal,
            OperationKind.MaintainAnnIndex, request.CommandId, OrleansSerialization.Serialize(request));
        var principal = fixture.Database.Store.Read(view => fixture.Database.Database.Principal(view,
            RequestCqrsAnnTestData.Principal, fixture.Clock.GetUtcNow()));
        var frames = new List<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>();
        var connection = fixture.Grains.GetGrain<IConnectionGrain>(fixture.ConnectionId);
        using var identity = new GrainRequestIdentityScope(fixture.Services, principal,
            id, request.CommandId, token, connectionId: fixture.ConnectionId);
        var reply = await GrainRequestStreamConsumer.DrainWithPurposeAsync(
            execution => ObserveAsync(connection.ExecuteStreamAsync(envelope, execution), frames, execution),
            fixture.Services.GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>(),
            id, fixture.Clock, fixture.RoutingOptions,
            new CqrsStreamPurpose(() => fixture.Codec.DecodeRequest(envelope, id)), token);
        await Assert.That(reply.Error).IsNull();
        await Assert.That(reply.SafeDetail).IsNull();
        var result = OrleansSerialization.Deserialize<GrainValue>(reply.Payload.Span).Value as AnnMaintenanceResult
            ?? throw new InvalidOperationException();
        if (retainedReplay)
        {
            await RequestCqrsAnnAssertions.RetainedFramesAsync(frames, id);
        }
        else
        {
            await RequestCqrsAnnAssertions.FramesAsync(frames, request, id);
        }
        await Assert.That(OrleansSerialization.Serialize(OrleansSerialization.Deserialize<GrainValue>(reply.Payload.Span))
            .AsSpan().SequenceEqual(reply.Payload.Span)).IsTrue();
        return (result, reply);
    }

    private static async IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> ObserveAsync(
        IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> original,
        List<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> frames,
        [EnumeratorCancellation] CancellationToken token)
    {
        await foreach (var frame in original.WithCancellation(token))
        {
            frames.Add(frame);
            yield return frame;
        }
    }
}
