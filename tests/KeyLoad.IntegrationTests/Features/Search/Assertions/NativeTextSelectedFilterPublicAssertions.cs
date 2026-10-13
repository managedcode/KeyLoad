using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextSelectedFilterPublicAssertions
{
    internal const string FirstJson = "{\"text\":\"needle\"}";
    internal const string SecondJson = "{\"text\":\"needle needle needle\"}";
    internal const string ThirdJson = "{\"text\":\"needle needle\"}";

    internal static async Task MatrixAsync(RequestCqrsRf3Callers caller,
        NativeTextSelectedFilterPublicState state, CancellationToken token)
    {
        var a = new DocumentResult(new(state.Published.Partition, state.Resource.Name, "a"), 1L, FirstJson, false, []);
        var b = new DocumentResult(new(state.Published.Partition, state.Resource.Name, "b"), 1L, SecondJson, false, []);
        var c = new DocumentResult(new(state.Published.Partition, state.Resource.Name, "c"), 1L, ThirdJson, false, []);
        await PageAsync(null, 3, [new(b, 1d / 61d, null), new(c, 1d / 62d, null), new(a, 1d / 63d, null)]);
        await PageAsync(null, 1, [new(b, 1d / 61d, null)]);
        await PageAsync(ImmutableArray.Create("a", "c", "unknown", "a"), 3, [new(c, 1d / 61d, null), new(a, 1d / 62d, null)]);
        await PageAsync(ImmutableArray.Create("a", "c", "unknown", "a"), 1, [new(c, 1d / 61d, null)]);
        await PageAsync(ImmutableArray.Create("c", "a"), 3, [new(c, 1d / 61d, null), new(a, 1d / 62d, null)]);
        await PageAsync(ImmutableArray.Create("a"), 1, [new(a, 1d / 61d, null)]);
        await PageAsync(ImmutableArray<string>.Empty, 3, []);
        await PageAsync(ImmutableArray.Create("unknown"), 3, []);

        async Task PageAsync(ImmutableArray<string>? allowed, int limit, RankedDocument[] expected)
        {
            var request = new SearchRequest(state.Published.Partition, state.Resource.Name,
                NativeTextMaintenanceRf3Scenario.Field, "needle", Limit: limit, AllowedIds: allowed,
                TextIndex: new(state.Build.Consumer, state.Build.IndexGeneration));
            await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.SearchAsync(request, token)));
            await NativeTextSelectedSchemaPublicAssertions.OutboxAsync(caller, state.Published.Partition, state.Outbox, token);
            await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<RankedDocument[]>(
                await caller.Mcp.CallAsync(McpCallerTools.SearchExecute, request, token))).Value);
            await NativeTextSelectedSchemaPublicAssertions.OutboxAsync(caller, state.Published.Partition, state.Outbox, token);
        }
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers caller,
        NativeTextSelectedFilterPublicState state, CancellationToken token)
    {
        await MatrixAsync(caller, state, token);
        await SqlRf3Protocol.EqualAsync(state.Publication, await McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.CommitAsync(state.Published, token)));
        await MatrixAsync(caller, state, token);
        await SqlRf3Protocol.EqualAsync(state.Publication, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await caller.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Published, token))).Value);
        await MatrixAsync(caller, state, token);
        foreach (var path in new[] { NativeTextMaintenancePath.Sdk, NativeTextMaintenancePath.Mcp })
        {
            var before = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
            var replay = await NativeTextMaintenanceRf3Call.ExecuteAsync(caller.Sdk, caller.Mcp, state.Build, path, token);
            var after = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
            await NativeTextMaintenanceRf3ReplayAssertions.StableAsync(replay, state.Built);
            await NativeTextMaintenanceRf3ReplayAssertions.SourceAsync(replay.Source!, state.Built.Source!, before, after);
            await MatrixAsync(caller, state, token);
        }
    }

    internal static async Task FirstAsync(TextIndexMaintenanceResult actual, TextIndexMaintenanceRequest request,
        ResourceDefinition resource, NodeStatus before, NodeStatus after)
    {
        await Assert.That(actual.CommandId).IsEqualTo(request.CommandId);
        await SqlRf3Protocol.EqualAsync(request.Consumer, actual.Consumer);
        await Assert.That(actual.IndexGeneration).IsEqualTo(1L);
        await Assert.That(actual.Phase).IsEqualTo(TextIndexMaintenancePhase.Completed);
        await Assert.That(actual.TrackedRecords).IsEqualTo(3);
        await Assert.That(actual.IndexedThroughSequence).IsEqualTo(3L);
        await Assert.That(actual.ReleasedConsumer).IsNull();
        await Assert.That(actual.IndexSha256).IsNotNull();
        await Assert.That(actual.IndexSha256!.Length).IsEqualTo(64);
        await Assert.That(actual.Checkpoint).IsNotNull();
        await Assert.That(actual.Checkpoint!.Checkpoint).IsEqualTo(3L);
        var receipt = actual.Checkpoint.Receipt;
        await Assert.That(receipt.CommandId).IsNotEqualTo(Guid.Empty);
        await Assert.That(receipt.Mutations.IsDefault).IsFalse();
        await SqlRf3Protocol.EqualAsync(Array.Empty<MutationReceipt>(), receipt.Mutations.ToArray());
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(request.Consumer.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(request.Placement.Incarnation);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(request.Placement.PlacementEpoch);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(actual.Source).IsNotNull();
        var source = actual.Source!;
        await Assert.That(source.NodeId).IsEqualTo(request.NodeId);
        await Assert.That(source.Incarnation).IsEqualTo(request.Placement.Incarnation);
        await Assert.That(source.SchemaVersion).IsEqualTo(1L);
        await Assert.That(source.PolicyEpoch).IsGreaterThan(0L);
        await Assert.That(source.ThroughSequence).IsEqualTo(3L);
        await Assert.That(source.ResourceSha256).IsEqualTo(Convert.ToHexStringLower(SHA256.HashData(NativeSerialization.Serialize(resource))));
        await NativeTextMaintenanceRf3ReplayAssertions.SourceAsync(source, source, before, after);
    }
}
