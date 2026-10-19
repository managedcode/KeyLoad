using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.Core;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class AggregateSnapshotRaceClusterCurrent
{
    internal static async Task<string> ImageAsync(RequestCqrsClusterCallers root, AggregateSnapshotRaceClusterState state, CancellationToken token)
    {
        var page = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAggregateReplayAsync(state.Scenario.ReadRequest(3), token));
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(state.Scenario.Partition, token));
        return JsonSerializer.Serialize(new
        {
            Page = page with
            {
                CutPosition = 0
            },
            Outbox = outbox
        }, JsonDefaults.Options);
    }
    internal static async Task ObserveAsync(RequestCqrsClusterCallers root, AggregateSnapshotRaceClusterState state,
        Func<Task> action, CancellationToken token)
    {
        var image = await ImageAsync(root, state, token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(action, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
            await Assert.That(await ImageAsync(root, state, token)).IsEqualTo(image), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task RequireAsync(RequestCqrsClusterCallers reader, AggregateSnapshotRaceClusterState state, CancellationToken token)
    {
        var command = state.Updated ? state.Healthy! : state.Winner!;
        var mutation = (StoreAggregateSnapshot)command.Mutations.Single();
        var snapshot = new AggregateSnapshotState(state.Scenario.Stream, state.Updated ? 2L : 1L,
            mutation.SourceRevision, AggregateReplayClusterTokens.Reducer, 1, mutation.StateJson, string.Empty);
        snapshot = snapshot with
        {
            Checksum = JsonData.Fingerprint(snapshot)
        };
        var expected = new AggregateReplayPage(state.Scenario.Stream, new(3, 1, 1), snapshot,
            [.. state.Events.Where(record => record.Revision > mutation.SourceRevision)], 0);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadAggregateReplayAsync(state.Scenario.ReadRequest(3), token));
        var mcp = (await McpCallerAssertions.SuccessAsync<AggregateReplayPage>(await reader.Mcp.CallAsync(
            McpCallerTools.StreamsReplay, state.Scenario.ReadRequest(3), token))).Value;
        foreach (var actual in new[] { sdk, mcp })
        {
            await Assert.That(actual.CutPosition).IsGreaterThan(0L);
            await SqlClusterProtocol.EqualAsync(expected, actual with
            {
                CutPosition = 0
            });
            await Assert.That(AggregateReplayReduction.Reduce(actual, Reducer(), IntegrationClientOptions.Replay(), token)).IsEqualTo("{\"count\":6}");
        }
        var full = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadAggregateReplayAsync(
            state.Scenario.ReadRequest(3) with
            {
                FromBeginning = true
            }, token));
        await SqlClusterProtocol.EqualAsync(new AggregateReplayPage(state.Scenario.Stream, new(3, 1, 1), null,
            state.Events, 0), full with
            {
                CutPosition = 0
            });
        await Assert.That(full.CutPosition).IsGreaterThan(0L);
        await Assert.That(AggregateReplayReduction.Reduce(full, Reducer(), IntegrationClientOptions.Replay(), token)).IsEqualTo("{\"count\":6}");
        var callbacks = 0;
        var incompatible = Reducer() with
        {
            EventSchemaVersion = 2,
            Apply = (json, _) =>
            {
                callbacks++;
                return json;
            }
        };
        var failure = Assert.ThrowsExactly<InvalidDataException>(() => AggregateReplayReduction.Reduce(full,
            incompatible, IntegrationClientOptions.Replay()));
        await Assert.That(failure.Message).IsEqualTo("The event schema does not match the reducer.");
        await Assert.That(callbacks).IsEqualTo(0);
    }
    private static AggregateReplayReducer Reducer() => new(AggregateReplayClusterTokens.Reducer, 1, 1, "{\"count\":0}", (json, record) =>
    {
        using var state = JsonDocument.Parse(json);
        using var input = JsonDocument.Parse(record.Data.PayloadJson);
        var count = state.RootElement.GetProperty(AggregateReplayClusterTokens.CountProperty).GetInt32()
            + input.RootElement.GetProperty(AggregateReplayClusterTokens.IncrementProperty).GetInt32();
        return "{\"count\":" + count.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";
    });
}
