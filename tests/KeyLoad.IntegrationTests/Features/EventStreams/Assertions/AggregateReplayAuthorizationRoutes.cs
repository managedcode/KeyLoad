using System.Net;
using KeyLoad.Client;
using KeyLoad.Core;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class AggregateReplayAuthorizationRoutes
{
    internal static async Task HealthyAsync(RequestCqrsRf3Callers callers,
        AggregateReplayAuthorizationOriginal original, CancellationToken token)
    {
        await HealthyPageAsync(callers, original, token);
        var command = original.SnapshotCommand;
        var sqlWrite = SqlRf3Protocol.Call(command.Partition, McpCallerTools.DocumentsCommit, command);
        await SqlRf3Protocol.EqualAsync(original.Receipt, await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.CommitAsync(command, token)));
        await SqlRf3Protocol.EqualAsync(original.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value);
        await SqlRf3Protocol.EqualAsync(original.Receipt,
            await SqlRf3Protocol.SdkAsync<CommitReceipt>(callers.Sdk, sqlWrite, token));
        await SqlRf3Protocol.EqualAsync(original.Receipt,
            await SqlRf3Protocol.McpAsync<CommitReceipt>(callers.Mcp, sqlWrite, token));
        await PageAsync(original.Page, await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.ReadAggregateReplayAsync(original.Request, token)));
    }

    internal static async Task HealthyPageAsync(RequestCqrsRf3Callers callers,
        AggregateReplayAuthorizationOriginal original, CancellationToken token)
    {
        var request = original.Request;
        var sqlRead = SqlRf3Protocol.Call(request.Stream.Partition, McpCallerTools.StreamsReplay, request);
        await PageAsync(original.Page, await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.ReadAggregateReplayAsync(request, token)));
        await PageAsync(original.Page, (await McpCallerAssertions.SuccessAsync<AggregateReplayPage>(
            await callers.Mcp.CallAsync(McpCallerTools.StreamsReplay, request, token))).Value);
        await PageAsync(original.Page, await SqlRf3Protocol.SdkAsync<AggregateReplayPage>(callers.Sdk, sqlRead, token));
        await PageAsync(original.Page, await SqlRf3Protocol.McpAsync<AggregateReplayPage>(callers.Mcp, sqlRead, token));
    }

    internal static async Task<AggregateReplayAuthorizationOriginal> FreshAsync(RequestCqrsRf3Callers callers,
        AggregateReplayAuthorizationOriginal original, CancellationToken token)
    {
        var previous = original.Page.Snapshot!;
        var mutation = (StoreAggregateSnapshot)original.SnapshotCommand.Mutations.Single();
        var command = original.SnapshotCommand with
        {
            CommandId = Guid.NewGuid(),
            Mutations = [mutation with { ExpectedSnapshotVersion = previous.SnapshotVersion,
                SourceRevision = previous.SourceRevision, StateJson = previous.StateJson }]
        };
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Token.Position).IsGreaterThan(original.Receipt.Token.Position);
        await SqlRf3Protocol.EqualAsync(new MutationReceipt(SnapshotMutation, previous.Stream.StreamSet,
            previous.Stream.StreamId, previous.SnapshotVersion + SnapshotVersionStep), receipt.Mutations.Single());
        var expected = previous with { SnapshotVersion = previous.SnapshotVersion + SnapshotVersionStep, Checksum = string.Empty };
        expected = expected with { Checksum = JsonData.Fingerprint(expected) };
        var page = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadAggregateReplayAsync(original.Request, token));
        await PageAsync(original.Page with { Snapshot = expected }, page);
        return new(original.Request, command, receipt, page);
    }

    internal static async Task EpochDeniedAsync(RequestCqrsRf3Callers callers,
        AggregateReplayAuthorizationOriginal original, string secret, CancellationToken token)
    {
        var command = original.SnapshotCommand;
        var denied = await callers.Sdk.CommitAsync(command, token);
        await Assert.That(denied.IsSuccess).IsFalse();
        await Assert.That(denied.Value).IsNull();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        var official = await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token);
        await McpCallerAssertions.ErrorAsync(official, ErrorCode.PermissionDenied, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(official, secret, AggregateReplayRf3Tokens.Secret);
        await McpCallerAssertions.DoesNotDiscloseAsync(official, secret, AggregateReplayRf3Tokens.PrivateHeader);
        var sql = SqlRf3Protocol.Call(command.Partition, McpCallerTools.DocumentsCommit, command);
        var sqlDenied = await callers.Sdk.ExecuteSqlAsync(sql, token);
        await Assert.That(sqlDenied.IsSuccess).IsFalse();
        await Assert.That(sqlDenied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        var sqlOfficial = await callers.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token);
        await McpCallerAssertions.ErrorAsync(sqlOfficial, ErrorCode.PermissionDenied, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(sqlOfficial, secret, AggregateReplayRf3Tokens.Secret);
        await McpCallerAssertions.DoesNotDiscloseAsync(sqlOfficial, secret, AggregateReplayRf3Tokens.PrivateHeader);
    }

    private const long SnapshotVersionStep = 1;
    private const string SnapshotMutation = "storeAggregateSnapshot";

    internal static async Task RefusedAsync(RequestCqrsRf3Callers callers,
        AggregateReplayAuthorizationOriginal original, string secret, CancellationToken token)
    {
        var denied = await callers.Sdk.CommitAsync(original.SnapshotCommand, token);
        await Assert.That(denied.IsSuccess).IsFalse();
        await Assert.That(denied.Value).IsNull();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Unauthenticated));
        await UnauthorizedAsync(() => callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit,
            original.SnapshotCommand, token), secret);
        var read = SqlRf3Protocol.Call(original.Request.Stream.Partition,
            McpCallerTools.StreamsReplay, original.Request);
        var write = SqlRf3Protocol.Call(original.SnapshotCommand.Partition,
            McpCallerTools.DocumentsCommit, original.SnapshotCommand);
        foreach (var request in new[] { read, write })
        {
            var refusal = await callers.Sdk.ExecuteSqlAsync(request, token);
            await Assert.That(refusal.IsSuccess).IsFalse();
            await Assert.That(refusal.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Unauthenticated));
            await UnauthorizedAsync(() => callers.Mcp.CallAsync(SqlOperationProtocol.ToolName, request, token), secret);
        }
    }

    private static async Task PageAsync(AggregateReplayPage original, AggregateReplayPage actual)
    {
        await Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(original.CutPosition);
        await SqlRf3Protocol.EqualAsync(original, actual with { CutPosition = original.CutPosition });
        await AggregateReplayRf3Assertions.SamePageAsync(actual, original,
            AggregateReplayRf3Tokens.InitialReducedCount, AggregateReplayRf3Tokens.TailCount,
            [AggregateReplayRf3Tokens.PaidEventId, AggregateReplayRf3Tokens.ShippedEventId],
            AggregateReplayRf3Tokens.InitialState, expectedSnapshotVersion: original.Snapshot!.SnapshotVersion);
    }

    private static async Task UnauthorizedAsync(Func<Task<ModelContextProtocol.Protocol.CallToolResult>> call,
        string secret)
    {
        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => call());
        await Assert.That(failure!.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(failure.Message.Contains(secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(failure.Message.Contains(AggregateReplayRf3Tokens.Secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(failure.Message.Contains(AggregateReplayRf3Tokens.PrivateHeader, StringComparison.Ordinal)).IsFalse();
    }
}
