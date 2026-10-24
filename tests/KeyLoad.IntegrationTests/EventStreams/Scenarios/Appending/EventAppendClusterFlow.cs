using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.EventStreams;

internal static class EventAppendClusterFlow
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
        await EventAppendClusterAssertions.ReceiptAsync(original, originalCommand, EventAppendClusterProtocol.SeedRevision);
        var expected = McpEventStreamScenario.InputEvents.ToArray();
        var initial = await EventAppendClusterAssertions.PageAsync(sdk, official, scenario, expected, token);
        var partialFailure = await RejectBoundariesAsync(sdk, official, scenario, originalCommand, fixture.AdminKey, token);
        await EventAppendClusterAuthorization.RunAsync(fixture, scenario, originalCommand, token);
        await EventAppendClusterAssertions.ReplayAsync(sdk, official, originalCommand, original, token);
        await EventAppendClusterAssertions.SameStreamAsync(initial,
            await EventAppendClusterAssertions.PageAsync(sdk, official, scenario, expected, token));
        var winner = await ConcurrentAsync(sdk, official, scenario, fixture.AdminKey, token);
        await EventAppendClusterAssertions.ReceiptIdentityAsync(original, winner.Receipt);
        expected = [.. expected, winner.Expected];
        var afterWinner = await EventAppendClusterAssertions.PageAsync(sdk, official, scenario, expected, token);
        await EventAppendClusterAssertions.PrefixAsync(initial, afterWinner);
        await EventAppendClusterAssertions.ReplayAsync(sdk, official, winner.Command, winner.Receipt, token);
        await EventAppendClusterAssertions.SameStreamAsync(afterWinner,
            await EventAppendClusterAssertions.PageAsync(sdk, official, scenario, expected, token));
        var healthyData = McpEventStreamScenario.InputEvents[EventAppendClusterProtocol.FirstEventIndex] with
        {
            EventId = EventAppendClusterProtocol.PartialEventId,
            PayloadJson = EventAppendClusterProtocol.HealthyPayload
        };
        var healthy = Command(scenario, [healthyData], ExpectedStreamRevision.Any);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(healthy, token));
        await EventAppendClusterAssertions.ReceiptAsync(receipt, healthy, EventAppendClusterProtocol.HealthyRevision);
        await EventAppendClusterAssertions.ReceiptIdentityAsync(winner.Receipt, receipt);
        EventData[] healthyExpected = [.. expected, McpEventStreamScenario.InputEvents[EventAppendClusterProtocol.FirstEventIndex] with
        { EventId = EventAppendClusterProtocol.PartialEventId, PayloadJson = EventAppendClusterProtocol.HealthyPayload }];
        var complete = await EventAppendClusterAssertions.PageAsync(sdk, official, scenario, healthyExpected, token);
        await EventAppendClusterAssertions.PrefixAsync(afterWinner, complete);
        await EventAppendClusterAssertions.ReplayAsync(sdk, official, healthy, receipt, token);
        await EventAppendClusterAssertions.ReplayAsync(sdk, official, originalCommand, original, token);
        await EventAppendClusterAssertions.RejectedAsync(sdk, official, partialFailure, ErrorCode.DuplicateEventId,
            EventAppendClusterProtocol.DuplicateDetail, fixture.AdminKey, token);
        await EventAppendClusterAssertions.SameStreamAsync(complete, await EventAppendClusterAssertions.PageAsync(sdk, official,
            scenario, healthyExpected, token));
    }

    private static async Task<CommandRequest> RejectBoundariesAsync(KeyLoadClient sdk, McpOfficialClient official,
        McpEventStreamScenario scenario, CommandRequest original, string credential, CancellationToken token)
    {
        var changed = McpEventStreamScenario.InputEvents[EventAppendClusterProtocol.FirstEventIndex] with
        {
            PayloadJson = EventAppendClusterProtocol.ChangedPayload
        };
        var changedCommand = Command(scenario, [changed], ExpectedStreamRevision.Any) with
        {
            CommandId = original.CommandId
        };
        await EventAppendClusterAssertions.RejectedAsync(sdk, official, changedCommand, ErrorCode.Conflict,
            EventAppendClusterProtocol.CommandDetail, credential, token);
        await EventAppendClusterAssertions.RejectedAsync(sdk, official, Command(scenario, [changed], ExpectedStreamRevision.Any),
            ErrorCode.Conflict, EventAppendClusterProtocol.EventDetail, credential, token);
        var partial = McpEventStreamScenario.InputEvents[EventAppendClusterProtocol.FirstEventIndex] with
        {
            EventId = EventAppendClusterProtocol.PartialEventId
        };
        var partialCommand = Command(scenario,
            [partial, McpEventStreamScenario.InputEvents[EventAppendClusterProtocol.SecondEventIndex]], ExpectedStreamRevision.Any);
        await EventAppendClusterAssertions.RejectedAsync(sdk, official, partialCommand,
            ErrorCode.DuplicateEventId, EventAppendClusterProtocol.DuplicateDetail, credential, token);
        await EventAppendClusterAssertions.RejectedAsync(sdk, official,
            Command(scenario, [partial], ExpectedStreamRevision.NoStream), ErrorCode.RevisionConflict,
            EventAppendClusterProtocol.RevisionDetail, credential, token);
        var stale = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new AppendEvents(McpEventStreamTokens.StreamSet, McpEventStreamTokens.StreamId, [partial],
                ExpectedStreamRevision.Any, McpEventStreamTokens.StaleGeneration)]);
        await EventAppendClusterAssertions.RejectedAsync(sdk, official, stale, ErrorCode.TokenInvalidated,
            EventAppendClusterProtocol.StaleDetail, credential, token);
        return partialCommand;
    }

    private static async Task<(CommandRequest Command, CommitReceipt Receipt, EventData Expected)> ConcurrentAsync(
        KeyLoadClient sdk, McpOfficialClient official, McpEventStreamScenario scenario, string credential, CancellationToken token)
    {
        var firstData = McpEventStreamScenario.InputEvents[EventAppendClusterProtocol.FirstEventIndex] with
        {
            EventId = EventAppendClusterProtocol.SdkEventId,
            PayloadJson = EventAppendClusterProtocol.SdkPayload
        };
        var secondData = McpEventStreamScenario.InputEvents[EventAppendClusterProtocol.FirstEventIndex] with
        {
            EventId = EventAppendClusterProtocol.McpEventId,
            PayloadJson = EventAppendClusterProtocol.McpPayload
        };
        var first = Command(scenario, [firstData], ExpectedStreamRevision.Exact(EventAppendClusterProtocol.SeedRevision));
        var second = Command(scenario, [secondData], ExpectedStreamRevision.Exact(EventAppendClusterProtocol.SeedRevision));
        var results = await EventAppendClusterCalls.ConcurrentAsync(sdk, official, first, second, token);
        await Assert.That((results.Sdk.Receipt is not null) != (results.Mcp.Receipt is not null)).IsTrue();
        var sdkWon = results.Sdk.Receipt is not null;
        var command = sdkWon ? first : second;
        var receipt = (sdkWon ? results.Sdk : results.Mcp).Receipt
            ?? throw new InvalidOperationException(EventAppendClusterProtocol.MissingReceipt);
        await EventAppendClusterAssertions.ReceiptAsync(receipt, command, EventAppendClusterProtocol.WinnerRevision);
        await EventAppendClusterAssertions.FailureAsync(sdkWon ? results.Mcp : results.Sdk, ErrorCode.RevisionConflict,
            EventAppendClusterProtocol.RevisionDetail, credential, official: sdkWon);
        await EventAppendClusterAssertions.RejectedAsync(sdk, official, sdkWon ? second : first, ErrorCode.RevisionConflict,
            EventAppendClusterProtocol.RevisionDetail, credential, token);
        return (command, receipt, McpEventStreamScenario.InputEvents[EventAppendClusterProtocol.FirstEventIndex] with
        {
            EventId = sdkWon ? EventAppendClusterProtocol.SdkEventId : EventAppendClusterProtocol.McpEventId,
            PayloadJson = sdkWon ? EventAppendClusterProtocol.SdkPayload : EventAppendClusterProtocol.McpPayload
        });
    }
}
