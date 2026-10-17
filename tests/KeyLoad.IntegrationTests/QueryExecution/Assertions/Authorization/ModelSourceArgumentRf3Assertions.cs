using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class ModelSourceArgumentRf3Assertions
{
    internal static async Task ObserveAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SqlModelViewRf3Scenario scenario, Func<Task> operation, CancellationToken token)
    {
        var image = await ImageAsync(sdk, scenario, token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
            await Assert.That(await ImageAsync(sdk, scenario, token)).IsEqualTo(image), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await SqlRf3Protocol.EqualAsync(scenario.Receipt,
                await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(scenario.Original, token)));
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var actual = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
                McpCallerTools.DocumentsCommit, scenario.Original, token))).Value;
            await SqlRf3Protocol.EqualAsync(scenario.Receipt, actual);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
            await Assert.That(await ImageAsync(sdk, scenario, token)).IsEqualTo(image), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task PageAsync(QueryPage actual, bool events)
    {
        QueryRow[] rows = events
            ? [new("event-created", 1L, """{"eventType":"OrderCreated"}""", false, []),
                new("event-paid", 2L, """{"eventType":"OrderPaid"}""", false, [])]
            : [new("message-created", 1L, """{"id":"message-created","state":"Ready"}""", false, []),
                new("message-paid", 1L, """{"id":"message-paid","state":"Ready"}""", false, [])];
        await SqlRf3Protocol.EqualAsync(new QueryPage([.. rows], null, actual.CutPosition,
            events ? "model-scan:events" : "model-scan:queue-messages"), actual);
        await Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(0L);
    }

    internal static async Task<string> ImageAsync(KeyLoadClient sdk, SqlModelViewRf3Scenario scenario, CancellationToken token)
    {
        var values = new List<string>();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var events = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadStreamAsync(new(scenario.Stream), token));
            await SqlRf3Protocol.EqualAsync(SqlModelViewRf3Scenario.ExpectedEvents.ToArray(),
                events.Events.Select(record => record.Data).ToArray());
            values.Add(System.Text.Json.JsonSerializer.Serialize(events, JsonDefaults.Options));
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var messages = await SqlModelViewRf3ParityAssertions.InspectBothAsync(sdk, scenario, token);
            values.Add(System.Text.Json.JsonSerializer.Serialize(new
            {
                First = messages.First,
                Second = messages.Second
            }, JsonDefaults.Options));
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var outbox = await McpCallerAssertions.SdkSuccessAsync(await sdk.OutboxStatusAsync(scenario.Partition, token));
            values.Add(System.Text.Json.JsonSerializer.Serialize(outbox, JsonDefaults.Options));
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return System.Text.Json.JsonSerializer.Serialize(values, JsonDefaults.Options);
    }
}
