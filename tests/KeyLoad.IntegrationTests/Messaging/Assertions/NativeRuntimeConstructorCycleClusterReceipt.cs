using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class NativeRuntimeConstructorCycleClusterReceipt
{
    private const string SagaMutation = "compareExchangeSaga";
    private const string GuidFormat = "N";

    internal static async Task CreatedAsync(ClusterFixture fixture, KeyLoadClient sdk, McpOfficialClient mcp,
        CommandRequest command, CommitReceipt receipt, CancellationToken token)
    {
        var mutation = (CompareExchangeSaga)command.Mutations.Single();
        var expected = new CommitReceipt(command.CommandId, receipt.Token,
            [new(SagaMutation, mutation.Lane.Queue, mutation.SagaId.ToString(GuidFormat), 1L)],
            DurabilityProfile.QuorumProcessDurable);
        await SameAsync(receipt, expected);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(
            ClusterFixturePhysicalShardIdentity.ReadProfile(fixture.Root).Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(1L);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await ReplayAsync(sdk, mcp, command, receipt, token);
    }

    internal static async Task ReplayAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        CommandRequest command, CommitReceipt original, CancellationToken token)
    {
        var sdkReplay = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, token));
        var reply = await mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token);
        var mcpReplay = await McpCallerAssertions.SuccessAsync<CommitReceipt>(reply);
        await SameAsync(sdkReplay, original);
        await SameAsync(mcpReplay.Value, original);
    }

    internal static async Task SameAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
