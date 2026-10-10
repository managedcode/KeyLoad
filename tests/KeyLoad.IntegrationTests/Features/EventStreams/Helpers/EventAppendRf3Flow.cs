using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventAppendRf3Flow
{
    internal static CommandRequest Command(McpEventStreamScenario scenario, EventData[] events,
        ExpectedStreamRevision expected) => new(Guid.NewGuid(), scenario.Partition,
            [new AppendEvents(McpEventStreamTokens.StreamSet, McpEventStreamTokens.StreamId, [.. events], expected)]);

    internal static async Task RunAsync(KeyLoadClient sdk, McpOfficialClient official, ClusterFixture fixture,
        CancellationToken token)
    {
        var scenario = await McpEventStreamScenario.CreateAsync(fixture, token);
        var originalCommand = scenario.AppendCommand(Guid.NewGuid());
        var original = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(originalCommand, token));
        await EventAppendRf3Assertions.ReceiptAsync(original, originalCommand, EventAppendRf3Protocol.SeedRevision);
        var expected = McpEventStreamScenario.ExpectedEvents.ToArray();
        var initial = await EventAppendRf3Assertions.PageAsync(sdk, official, scenario, expected, original, token);
        var partialFailure = await RejectBoundariesAsync(sdk, official, scenario, originalCommand, fixture.AdminKey, token);
        await EventAppendRf3Authorization.RunAsync(fixture, scenario, originalCommand, original, token);
        await EventAppendRf3Assertions.ReplayAsync(sdk, official, originalCommand, original, token);
        await EventAppendRf3Assertions.SameStreamAsync(initial,
            await EventAppendRf3Assertions.PageAsync(sdk, official, scenario, expected, original, token));
        var winner = await ConcurrentAsync(sdk, official, scenario, fixture.AdminKey, token);
        await EventAppendRf3Assertions.ReceiptIdentityAsync(original, winner.Receipt);
        expected = [.. expected, winner.Expected];
        var afterWinner = await EventAppendRf3Assertions.PageAsync(sdk, official, scenario, expected, winner.Receipt, token);
        await EventAppendRf3Assertions.PrefixAsync(initial, afterWinner);
        await EventAppendRf3Assertions.ReplayAsync(sdk, official, winner.Command, winner.Receipt, token);
        await EventAppendRf3Assertions.SameStreamAsync(afterWinner,
            await EventAppendRf3Assertions.PageAsync(sdk, official, scenario, expected, winner.Receipt, token));
        var healthyData = McpEventStreamScenario.InputEvents[EventAppendRf3Protocol.FirstEventIndex] with
        {
            EventId = EventAppendRf3Protocol.PartialEventId,
            PayloadJson = EventAppendRf3Protocol.HealthyPayload
        };
        var healthy = Command(scenario, [healthyData], ExpectedStreamRevision.Any);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(healthy, token));
        await EventAppendRf3Assertions.ReceiptAsync(receipt, healthy, EventAppendRf3Protocol.HealthyRevision);
        await EventAppendRf3Assertions.ReceiptIdentityAsync(winner.Receipt, receipt);
        EventData[] healthyExpected = [.. expected, McpEventStreamScenario.ExpectedEvents[EventAppendRf3Protocol.FirstEventIndex] with
        { EventId = EventAppendRf3Protocol.PartialEventId, PayloadJson = EventAppendRf3Protocol.HealthyExpected }];
        var complete = await EventAppendRf3Assertions.PageAsync(sdk, official, scenario, healthyExpected, receipt, token);
        await EventAppendRf3Assertions.PrefixAsync(afterWinner, complete);
        await EventAppendRf3Assertions.ReplayAsync(sdk, official, healthy, receipt, token);
        await EventAppendRf3Assertions.ReplayAsync(sdk, official, originalCommand, original, token);
        await EventAppendRf3Assertions.RejectedAsync(sdk, official, partialFailure, ErrorCode.DuplicateEventId,
            EventAppendRf3Protocol.DuplicateDetail, fixture.AdminKey, token);
        await EventAppendRf3Assertions.SameStreamAsync(complete, await EventAppendRf3Assertions.PageAsync(sdk, official,
            scenario, healthyExpected, receipt, token));
    }

    private static async Task<CommandRequest> RejectBoundariesAsync(KeyLoadClient sdk, McpOfficialClient official,
        McpEventStreamScenario scenario, CommandRequest original, string credential, CancellationToken token)
    {
        var changed = McpEventStreamScenario.InputEvents[EventAppendRf3Protocol.FirstEventIndex] with { PayloadJson = EventAppendRf3Protocol.ChangedPayload };
        var changedCommand = Command(scenario, [changed], ExpectedStreamRevision.Any) with { CommandId = original.CommandId };
        await EventAppendRf3Assertions.RejectedAsync(sdk, official, changedCommand, ErrorCode.Conflict,
            EventAppendRf3Protocol.CommandDetail, credential, token);
        await EventAppendRf3Assertions.RejectedAsync(sdk, official, Command(scenario, [changed], ExpectedStreamRevision.Any),
            ErrorCode.Conflict, EventAppendRf3Protocol.EventDetail, credential, token);
        var partial = McpEventStreamScenario.InputEvents[EventAppendRf3Protocol.FirstEventIndex] with { EventId = EventAppendRf3Protocol.PartialEventId };
        var partialCommand = Command(scenario,
            [partial, McpEventStreamScenario.InputEvents[EventAppendRf3Protocol.SecondEventIndex]], ExpectedStreamRevision.Any);
        await EventAppendRf3Assertions.RejectedAsync(sdk, official, partialCommand,
            ErrorCode.DuplicateEventId, EventAppendRf3Protocol.DuplicateDetail, credential, token);
        await EventAppendRf3Assertions.RejectedAsync(sdk, official,
            Command(scenario, [partial], ExpectedStreamRevision.NoStream), ErrorCode.RevisionConflict,
            EventAppendRf3Protocol.RevisionDetail, credential, token);
        var stale = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new AppendEvents(McpEventStreamTokens.StreamSet, McpEventStreamTokens.StreamId, [partial],
                ExpectedStreamRevision.Any, McpEventStreamTokens.StaleGeneration)]);
        await EventAppendRf3Assertions.RejectedAsync(sdk, official, stale, ErrorCode.TokenInvalidated,
            EventAppendRf3Protocol.StaleDetail, credential, token);
        return partialCommand;
    }

    private static async Task<(CommandRequest Command, CommitReceipt Receipt, EventData Expected)> ConcurrentAsync(
        KeyLoadClient sdk, McpOfficialClient official, McpEventStreamScenario scenario, string credential, CancellationToken token)
    {
        var firstData = McpEventStreamScenario.InputEvents[EventAppendRf3Protocol.FirstEventIndex] with
        {
            EventId = EventAppendRf3Protocol.SdkEventId,
            PayloadJson = EventAppendRf3Protocol.SdkPayload
        };
        var secondData = McpEventStreamScenario.InputEvents[EventAppendRf3Protocol.FirstEventIndex] with
        {
            EventId = EventAppendRf3Protocol.McpEventId,
            PayloadJson = EventAppendRf3Protocol.McpPayload
        };
        var first = Command(scenario, [firstData], ExpectedStreamRevision.Exact(EventAppendRf3Protocol.SeedRevision));
        var second = Command(scenario, [secondData], ExpectedStreamRevision.Exact(EventAppendRf3Protocol.SeedRevision));
        var results = await EventAppendRf3Calls.ConcurrentAsync(sdk, official, first, second, token);
        await Assert.That((results.Sdk.Receipt is not null) != (results.Mcp.Receipt is not null)).IsTrue();
        var sdkWon = results.Sdk.Receipt is not null;
        var command = sdkWon ? first : second;
        var receipt = (sdkWon ? results.Sdk : results.Mcp).Receipt
            ?? throw new InvalidOperationException(EventAppendRf3Protocol.MissingReceipt);
        await EventAppendRf3Assertions.ReceiptAsync(receipt, command, EventAppendRf3Protocol.WinnerRevision);
        await EventAppendRf3Assertions.FailureAsync(sdkWon ? results.Mcp : results.Sdk, ErrorCode.RevisionConflict,
            EventAppendRf3Protocol.RevisionDetail, credential);
        await EventAppendRf3Assertions.RejectedAsync(sdk, official, sdkWon ? second : first, ErrorCode.RevisionConflict,
            EventAppendRf3Protocol.RevisionDetail, credential, token);
        return (command, receipt, McpEventStreamScenario.ExpectedEvents[EventAppendRf3Protocol.FirstEventIndex] with
        {
            EventId = sdkWon ? EventAppendRf3Protocol.SdkEventId : EventAppendRf3Protocol.McpEventId,
            PayloadJson = sdkWon ? EventAppendRf3Protocol.SdkExpected : EventAppendRf3Protocol.McpExpected
        });
    }
}
