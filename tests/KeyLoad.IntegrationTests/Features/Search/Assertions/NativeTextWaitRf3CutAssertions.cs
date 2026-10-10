using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextWaitRf3CutAssertions
{
    internal const string Sdk = "SDK";
    internal const string Mcp = "MCP";
    internal const string SdkSql = "Q1 SDK";
    internal const string McpSql = "Q1 MCP";

    internal static async Task RunAsync(string route, Func<Task<WaitForIndexResult>> read,
        KeyLoadClient administrator, WaitForIndexRequest request, long schema, long epoch, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.StatusAsync(token));
        var actual = await read();
        var after = await McpCallerAssertions.SdkSuccessAsync(await administrator.StatusAsync(token));
        await OwnerAsync(before, after);
        await Assert.That(before.Incarnation).IsEqualTo(request.MinimumToken.Incarnation);
        await Assert.That(actual.AppliedToken).IsNotNull();
        var applied = actual.AppliedToken;
        await Assert.That(applied.Position).IsGreaterThanOrEqualTo(before.Applied);
        await Assert.That(applied.Position).IsLessThanOrEqualTo(after.Applied);
        await Assert.That(applied.Position).IsGreaterThanOrEqualTo(request.MinimumToken.Position);
        var expected = new WaitForIndexResult(new(before.Incarnation, request.Partition.AtomicPartitionId,
            applied.Position, request.MinimumToken.OwnershipEpoch), schema, epoch);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected)))
            .IsTrue().Because(Describe(route, expected, actual));
    }

    internal static async Task OwnerAsync(NodeStatus before, NodeStatus after)
    {
        await Assert.That(after.NodeId).IsEqualTo(before.NodeId);
        await Assert.That(after.Incarnation).IsEqualTo(before.Incarnation);
        await Assert.That(after.ReadGeneration).IsEqualTo(before.ReadGeneration);
        await Assert.That(before.RoutingReady).IsTrue();
        await Assert.That(after.RoutingReady).IsTrue();
    }

    private static string Describe(string route, WaitForIndexResult expected, WaitForIndexResult actual)
        => FormattableString.Invariant($"WaitForIndex {route}: schema={expected.SchemaVersion}/{actual.SchemaVersion}; policy={expected.PolicyEpoch}/{actual.PolicyEpoch}; incarnationEqual={expected.AppliedToken.Incarnation == actual.AppliedToken.Incarnation}; partitionEqual={expected.AppliedToken.AtomicPartitionId == actual.AppliedToken.AtomicPartitionId}; ownershipEqual={expected.AppliedToken.OwnershipEpoch == actual.AppliedToken.OwnershipEpoch}.");
}
