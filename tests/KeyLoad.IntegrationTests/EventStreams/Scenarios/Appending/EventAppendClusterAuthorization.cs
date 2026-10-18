using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventAppendClusterAuthorization
{
    internal static async Task RunAsync(ClusterFixture fixture, McpEventStreamScenario scenario,
        CommandRequest retained, CancellationToken token)
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
                var before = await EventAppendClusterAssertions.PageAsync(reader, official, scenario, expected, token);
                var fresh = EventAppendClusterFlow.Command(scenario, [McpEventStreamScenario.InputEvents[EventAppendClusterProtocol.FirstEventIndex] with
                    { EventId = EventAppendClusterProtocol.PartialEventId }], ExpectedStreamRevision.Any);
                await EventAppendClusterAssertions.RejectedAsync(reader, official, fresh, ErrorCode.PermissionDenied,
                    EventAppendClusterProtocol.DeniedDetail, identity.Secret, token);
                await EventAppendClusterAssertions.RejectedAsync(reader, official, retained, ErrorCode.PermissionDenied,
                    EventAppendClusterProtocol.DeniedDetail, identity.Secret, token);
                await EventAppendClusterAssertions.SameStreamAsync(before,
                    await EventAppendClusterAssertions.PageAsync(reader, official, scenario, expected, token));
            }, failures).ConfigureAwait(false);
            if (official is { } owned)
            {
                await ServerFailureObserver.ObserveAsync(() => owned.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
            }
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
