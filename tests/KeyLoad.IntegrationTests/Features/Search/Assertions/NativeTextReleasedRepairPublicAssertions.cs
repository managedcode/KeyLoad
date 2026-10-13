using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextReleasedRepairPublicAssertions
{
    internal static async Task FirstAsync(TextIndexMaintenanceResult actual, TextIndexMaintenanceRequest request,
        NodeStatus before, NodeStatus after)
    {
        await NativeTextMaintenanceRf3Assertions.ResultAsync(actual, request);
        await Assert.That(actual.TrackedRecords).IsEqualTo(2);
        await Assert.That(actual.IndexedThroughSequence).IsEqualTo(2L);
        await Assert.That(actual.Checkpoint).IsNotNull();
        await Assert.That(actual.Checkpoint!.Checkpoint).IsEqualTo(2L);
        await Assert.That(actual.Checkpoint.AlreadyProcessed).IsFalse();
        await Assert.That(actual.Checkpoint.Receipt.CommandId).IsNotEqualTo(Guid.Empty);
        await Assert.That(actual.Checkpoint.Receipt.Token.AtomicPartitionId).IsEqualTo(request.Consumer.Partition.AtomicPartitionId);
        await Assert.That(actual.Checkpoint.Receipt.Token.Incarnation).IsEqualTo(request.Placement.Incarnation);
        await Assert.That(actual.Checkpoint.Receipt.Token.OwnershipEpoch).IsEqualTo(request.Placement.PlacementEpoch);
        await Assert.That(actual.Checkpoint.Receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(actual.Checkpoint.Receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(actual.Checkpoint.Receipt.Mutations.IsDefault).IsFalse();
        await SqlRf3Protocol.EqualAsync(Array.Empty<MutationReceipt>(), actual.Checkpoint.Receipt.Mutations.ToArray());
        await Assert.That(actual.Source!.SchemaVersion).IsEqualTo(1L);
        await NativeTextMaintenanceRf3ReplayAssertions.SourceAsync(actual.Source, actual.Source, before, after, 2L);
    }

    internal static async Task CurrentAsync(RequestCqrsRf3Callers caller, NativeTextSelectedSchemaPublicState original,
        NativeTextSelectedSchemaPublicState? current, OutboxStatus baseline, CancellationToken token)
    {
        await NativeTextSelectedSchemaPublicAssertions.PagesAsync(caller, current ?? original, current is not null, token);
        await NativeTextSelectedSchemaPublicAssertions.PagesAsync(caller, original, false, token);
        await NativeTextSelectedSchemaPublicAssertions.OutboxAsync(caller, original.Scenario.Partition, baseline, token);
    }

    internal static async Task RefuseAsync(RequestCqrsRf3Callers caller, NativeTextSelectedSchemaPublicState original,
        NativeTextSelectedSchemaPublicState? current, OutboxStatus baseline, CancellationToken token)
    {
        var request = new SearchRequest(original.Scenario.Partition, original.Resource.Name,
            NativeTextMaintenanceRf3Scenario.Field, "ПРИВІТ", TextIndex: new(original.Build.Consumer, original.Build.IndexGeneration));
        var sdk = await caller.Sdk.SearchAsync(request, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value).IsNull();
        await Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.HistoryUnavailable));
        await CurrentAsync(caller, original, current, baseline, token);
        await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(McpCallerTools.SearchExecute, request, token),
            ErrorCode.HistoryUnavailable, dispatched: true);
        await CurrentAsync(caller, original, current, baseline, token);
        var old = await caller.Sdk.MaintainTextIndexAsync(original.Build, token);
        await Assert.That(old.IsFailed).IsTrue();
        await Assert.That(old.Value).IsNull();
        await Assert.That(old.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.TokenInvalidated));
        await CurrentAsync(caller, original, current, baseline, token);
        await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(TextIndexMaintenanceProtocol.ToolName, original.Build, token),
            ErrorCode.TokenInvalidated, dispatched: true);
        await CurrentAsync(caller, original, current, baseline, token);
    }
}
