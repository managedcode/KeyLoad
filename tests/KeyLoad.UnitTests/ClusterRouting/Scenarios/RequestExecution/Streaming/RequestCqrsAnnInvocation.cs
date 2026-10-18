using System.Runtime.CompilerServices;
using KeyLoad.Clustering;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsAnnInvocation
{
    internal static async Task<(AnnMaintenanceResult Result, GrainOperationReply Reply)> RunAsync(
        RequestCqrsHostedRuntimeFixture fixture, AnnMaintenanceRequest request, CancellationToken token, bool retainedReplay = false)
    {
        var id = Guid.NewGuid();
        var signed = fixture.Codec.CreateCommand(id, RequestCqrsAnnTestData.Principal,
            OperationKind.MaintainAnnIndex, request.CommandId, NativeSerialization.Serialize(request));
        var principal = fixture.Database.Store.Read(view => fixture.Database.Database.Principal(view,
            RequestCqrsAnnTestData.Principal, fixture.Clock.GetUtcNow()));
        var frames = new List<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>();
        var connection = fixture.Grains.GetGrain<IConnectionGrain>(fixture.ConnectionId);
        using var identity = new GrainRequestIdentityScope(fixture.Services, principal,
            id, request.CommandId, token, connectionId: fixture.ConnectionId);
        var reply = await GrainRequestStreamConsumer.DrainWithPurposeAsync(
            execution => ObserveAsync(connection.ExecuteStreamAsync(signed, execution), frames, execution),
            fixture.Services.GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>(),
            id, fixture.Clock, fixture.RoutingOptions,
            new NativeCqrsStreamPurpose(() => fixture.Codec.VerifyRequest(signed, id)), token);
        await Assert.That(reply.Error).IsNull();
        await Assert.That(reply.SafeDetail).IsNull();
        var result = NativeSerialization.Deserialize<GrainValue>(reply.Payload.Span).Value as AnnMaintenanceResult
            ?? throw new InvalidOperationException();
        if (retainedReplay)
        {
            await RequestCqrsAnnAssertions.RetainedFramesAsync(frames, id);
        }
        else
        {
            await RequestCqrsAnnAssertions.FramesAsync(frames, request, id);
        }
        await Assert.That(NativeSerialization.Serialize(NativeSerialization.Deserialize<GrainValue>(reply.Payload.Span))
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
