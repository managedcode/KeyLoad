using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventAppendRf3Authorization
{
    internal static async Task RunAsync(ClusterFixture fixture, McpEventStreamScenario scenario,
        CommandRequest retained, CommitReceipt original, CancellationToken token)
    {
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            McpEventStreamTokens.StreamSet, Capability.EventsRead, token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
            var reader = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
            McpOfficialClient? official = null;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                official = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, identity.Secret, token);
                var expected = McpEventStreamScenario.ExpectedEvents.ToArray();
                var before = await EventAppendRf3Assertions.PageAsync(reader, official, scenario, expected, original, token);
                var fresh = EventAppendRf3Flow.Command(scenario, [McpEventStreamScenario.InputEvents[EventAppendRf3Protocol.FirstEventIndex] with
                    { EventId = EventAppendRf3Protocol.PartialEventId }], ExpectedStreamRevision.Any);
                await EventAppendRf3Assertions.RejectedAsync(reader, official, fresh, ErrorCode.PermissionDenied,
                    EventAppendRf3Protocol.DeniedDetail, identity.Secret, token);
                await EventAppendRf3Assertions.RejectedAsync(reader, official, retained, ErrorCode.PermissionDenied,
                    EventAppendRf3Protocol.DeniedDetail, identity.Secret, token);
                await EventAppendRf3Assertions.SameStreamAsync(before,
                    await EventAppendRf3Assertions.PageAsync(reader, official, scenario, expected, original, token));
            }, failures).ConfigureAwait(false);
            if (official is { } owned)
            { await ServerFailureObserver.ObserveAsync(() => owned.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
