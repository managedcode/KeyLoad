using System.Runtime.CompilerServices;
using KeyLoad.Clustering;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class RequestCqrsAnnInvocation
{
    internal static async Task<(AnnMaintenanceResult Result, GrainOperationReply Reply)> RunAsync(
        RequestCqrsHostedRuntimeFixture fixture, AnnMaintenanceRequest request, CancellationToken token, bool retainedReplay = false)
    {
        var id = Guid.NewGuid();
        var envelope = fixture.Codec.CreateCommand(id, RequestCqrsAnnTestData.Principal,
            OperationKind.MaintainAnnIndex, request.CommandId, request);
        var principal = fixture.Database.Store.Read(view => fixture.Database.Database.Principal(view,
            RequestCqrsAnnTestData.Principal, fixture.Clock.GetUtcNow()));
        var frames = new List<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>();
        var connection = fixture.Grains.GetGrain<IConnectionGrain>(fixture.ConnectionId);
        using var identity = new GrainRequestIdentityScope(fixture.Services, principal,
            id, request.CommandId, token, connectionId: fixture.ConnectionId);
        var reply = await GrainRequestStreamConsumer.DrainWithPurposeAsync(
            execution => ObserveAsync(connection.ExecuteStreamAsync(envelope, execution), frames, execution),
            id,
            new CqrsStreamPurpose(() => fixture.Codec.DecodeRequest(envelope, id)), token);
        await Assert.That(reply.Error).IsNull();
        await Assert.That(reply.SafeDetail).IsNull();
        var result = GrainReplyFactory.RequireValue(reply).Value as AnnMaintenanceResult
            ?? throw new InvalidOperationException();
        if (retainedReplay)
        {
            await RequestCqrsAnnAssertions.RetainedFramesAsync(frames, id);
        }
        else
        {
            await RequestCqrsAnnAssertions.FramesAsync(frames, request, id);
        }
        var delivered = OrleansSerialization.Deserialize<GrainOperationReply>(OrleansSerialization.Serialize(reply));
        await Assert.That(OrleansSerialization.Serialize(GrainReplyFactory.RequireValue(delivered))
            .AsSpan().SequenceEqual(OrleansSerialization.Serialize(new GrainValue(result)))).IsTrue();
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
