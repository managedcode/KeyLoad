using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventFeedFirstOpenRf3Assertions
{
    private const long FirstRevision = 1;
    private const long FirstCoverage = 1;

    internal static async Task OpenAsync(EventFeedControlRequest request, EventFeedControlResult actual)
    {
        await Assert.That(actual.Version).IsEqualTo(EventFeedOpenProtocol.Version);
        await Assert.That(actual.PublicCommandId).IsEqualTo(request.CommandId);
        await Assert.That(actual.MapId).IsEqualTo(request.MapId);
        await Assert.That(actual.Revision).IsEqualTo(FirstRevision);
        await Assert.That(actual.CoverageGeneration).IsEqualTo(FirstCoverage);
        await Assert.That(actual.Released).IsFalse();
        await Assert.That(actual.Retained).IsTrue();
        await Assert.That(actual.Cursor).IsNotNull();
        await Assert.That(actual.OfferDigest).IsNull();
        await Assert.That(actual.Pages.IsEmpty).IsTrue();
        await Assert.That(actual.Receipt.CommandId).IsNotEqualTo(request.CommandId);
        await Assert.That(actual.Receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(actual.Receipt.Token.AtomicPartitionId).IsEqualTo(request.ControlPartition.AtomicPartitionId);
        await Assert.That(actual.Receipt.Token.Position).IsGreaterThan(0);
    }

    internal static async Task DeniedAsync(RequestCqrsRf3Callers callers, EventFeedControlRequest request,
        CancellationToken token)
    {
        var sdk = await callers.Sdk.OpenEventFeedAsync(request, token);
        await Assert.That(sdk.IsSuccess).IsFalse();
        await Assert.That(sdk.Problem!.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
        var official = await callers.Mcp.CallAsync(EventFeedOpenProtocol.Tool, request, token);
        await McpCallerAssertions.ErrorAsync(official, ErrorCode.PermissionDenied, dispatched: true);
        var sql = await callers.Sdk.ExecuteSqlAsync(SqlRf3Protocol.Call(request.ControlPartition,
            EventFeedOpenProtocol.Tool, request), token);
        await Assert.That(sql.IsSuccess).IsFalse();
        await Assert.That(sql.Problem!.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
    }
}
