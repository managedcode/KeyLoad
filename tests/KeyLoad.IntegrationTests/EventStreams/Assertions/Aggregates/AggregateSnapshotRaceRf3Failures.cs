using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class AggregateSnapshotRaceRf3Failures
{
    internal static async Task OutcomeAsync(EventAppendRf3Outcome actual, ErrorCode code, string detail, bool official)
    {
        await Assert.That(actual.Receipt).IsNull();
        await Assert.That(actual.Error).IsEqualTo(code);
        var expectedDetail = official ? code == ErrorCode.PermissionDenied
            ? "The authenticated principal is not permitted to perform this operation."
            : "The database operation could not be completed." : detail;
        await Assert.That(actual.Detail).IsEqualTo(expectedDetail);
        using var actualJson = JsonDocument.Parse(actual.ProblemJson!);
        await Assert.That(JsonElement.DeepEquals(actualJson.RootElement,
            JsonSerializer.SerializeToElement(Errors.Problem(code, expectedDetail), JsonDefaults.Options))).IsTrue();
        await Assert.That(actual.ProblemJson!.Contains(AggregateReplayRf3Tokens.Secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(actual.ProblemJson.Contains(AggregateReplayRf3Tokens.PrivateHeader, StringComparison.Ordinal)).IsFalse();
    }
    internal static async Task CommandsAsync(RequestCqrsRf3Callers reader, AggregateSnapshotRaceRf3State state,
        bool fenced, CancellationToken token)
    {
        if (fenced)
        {
            foreach (var command in state.Commands)
            {
                await RejectedAsync(reader, command, ErrorCode.PermissionDenied,
                    "The principal policy changed since this command was evaluated.", token);
            }
        }
        else
        {
            await RejectedAsync(reader, state.Loser!, ErrorCode.RevisionConflict,
                "The aggregate snapshot version has changed.", token);
            await ReplayAsync(reader, state.Winner!, state.WinnerReceipt!, token);
        }
    }
    internal static async Task ReplayAsync(RequestCqrsRf3Callers reader, CommandRequest command,
        CommitReceipt receipt, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.CommitAsync(command, token)));
        await SqlRf3Protocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await reader.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value);
    }
    private static async Task RejectedAsync(RequestCqrsRf3Callers reader, CommandRequest command,
        ErrorCode code, string detail, CancellationToken token)
    {
        await OutcomeAsync(await EventAppendRf3Calls.SdkAsync(reader.Sdk, command, token), code, detail, false);
        await OutcomeAsync(await EventAppendRf3Calls.McpAsync(reader.Mcp, command, token), code, detail, true);
    }
    internal static async Task ReadDeniedAsync(RequestCqrsRf3Callers reader, AggregateSnapshotRaceRf3State state, CancellationToken token)
    {
        var sdk = await reader.Sdk.ReadAggregateReplayAsync(state.Scenario.ReadRequest(3), token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value).IsNull();
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.PermissionDenied,
            "Complete replay input requires every raw-read and raw-use grant."), sdk.Problem);
        var mcp = await reader.Mcp.CallAsync(McpCallerTools.StreamsReplay, state.Scenario.ReadRequest(3), token);
        await McpCallerAssertions.ErrorAsync(mcp, ErrorCode.PermissionDenied, dispatched: true);
        await Assert.That(JsonElement.DeepEquals(mcp.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error),
            JsonSerializer.SerializeToElement(Errors.Problem(ErrorCode.PermissionDenied,
                "The authenticated principal is not permitted to perform this operation."), JsonDefaults.Options))).IsTrue();
        await McpCallerAssertions.DoesNotDiscloseAsync(mcp, state.Scenario.WorkerSecret, AggregateReplayRf3Tokens.Secret);
        await McpCallerAssertions.DoesNotDiscloseAsync(mcp, state.Scenario.WorkerSecret, AggregateReplayRf3Tokens.PrivateHeader);
    }
}
