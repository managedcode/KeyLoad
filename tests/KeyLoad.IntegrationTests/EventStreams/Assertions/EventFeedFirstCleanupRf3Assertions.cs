using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventFeedFirstCleanupRf3Assertions
{
    private const int Version = 1;
    private const long InitialRevision = 0;
    private const long NoPosition = 0;

    internal static async Task ResultAsync(EventFeedControlRequest request, EventFeedControlResult result, bool released)
    {
        await Assert.That(result.Version).IsEqualTo(Version);
        await Assert.That(result.PublicCommandId).IsEqualTo(request.CommandId);
        await Assert.That(result.MapId).IsEqualTo(request.MapId);
        await Assert.That(result.Revision).IsEqualTo(InitialRevision);
        await Assert.That(result.CoverageGeneration).IsEqualTo(InitialRevision);
        await Assert.That(result.Cursor).IsNull();
        await Assert.That(result.OfferDigest).IsNull();
        await Assert.That(result.Pages.IsEmpty).IsTrue();
        await Assert.That(result.Released).IsEqualTo(released);
        await Assert.That(result.Retained).IsEqualTo(!released);
        await Assert.That(result.Receipt.CommandId != request.CommandId).IsTrue();
        await Assert.That(result.Receipt.Token.AtomicPartitionId).IsEqualTo(request.ControlPartition.AtomicPartitionId);
        await Assert.That(result.Receipt.Token.Position > NoPosition).IsTrue();
        await Assert.That(result.Receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(!result.Receipt.Mutations.IsDefault).IsTrue();
    }

    internal static Task<EventFeedControlRequest> DeniedAsync(TwoRf3MembershipWave wave, string credential,
        EventFeedControlRequest request, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1, credential,
            async callers =>
            {
                var sdk = await callers.Sdk.ReleaseEventFeedAsync(request, token);
                await Assert.That(sdk.IsSuccess).IsFalse();
                await Assert.That(sdk.Problem!.ErrorCode).IsEqualTo(ErrorCode.CursorExpired.ToString());
                await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(EventFeedSettlementProtocol.ReleaseTool,
                    request, token), ErrorCode.CursorExpired, dispatched: true);
                var sql = await callers.Sdk.ExecuteSqlAsync(SqlRf3Protocol.Call(request.ControlPartition,
                    EventFeedSettlementProtocol.ReleaseTool, request), token);
                await Assert.That(sql.IsSuccess).IsFalse();
                await Assert.That(sql.Problem!.ErrorCode).IsEqualTo(ErrorCode.CursorExpired.ToString());
                return request;
            }, token);
}
