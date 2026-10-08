using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextMaintenanceRf3Mutation
{
    private const long OriginalRevision = 1;
    private const long ChangedRevision = 2;
    private const int MutationCount = 2;

    internal static async Task<NativeTextMaintenanceOriginalMutation> CommitAsync(KeyLoadClient sdk,
        NativeTextMaintenanceRf3Scenario scenario, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new PutDocument(NativeTextMaintenanceRf3Scenario.Collection, NativeTextMaintenanceRf3Scenario.Ukrainian,
                NativeTextMaintenanceRf3Scenario.ChangedJson, ExpectedRevision: OriginalRevision),
                new DeleteDocument(NativeTextMaintenanceRf3Scenario.Collection, NativeTextMaintenanceRf3Scenario.English,
                    ExpectedRevision: OriginalRevision)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(MutationCount);
        MutationReceipt[] literal =
            [new("putDocument", NativeTextMaintenanceRf3Scenario.Collection, NativeTextMaintenanceRf3Scenario.Ukrainian, ChangedRevision),
                new("deleteDocument", NativeTextMaintenanceRf3Scenario.Collection, NativeTextMaintenanceRf3Scenario.English, ChangedRevision)];
        for (var index = 0; index < MutationCount; index++)
        {
            await Assert.That(NativeSerialization.Serialize(receipt.Mutations[index])
                .SequenceEqual(NativeSerialization.Serialize(literal[index]))).IsTrue();
        }
        return new(command, receipt);
    }

    internal static async Task ReplayAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        NativeTextMaintenanceOriginalMutation original, CancellationToken token)
    {
        var direct = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(original.Command, token));
        var official = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
            McpCallerTools.DocumentsCommit, original.Command, token))).Value;
        var bytes = NativeSerialization.Serialize(original.Receipt);
        await Assert.That(NativeSerialization.Serialize(direct).SequenceEqual(bytes)).IsTrue();
        await Assert.That(NativeSerialization.Serialize(official).SequenceEqual(bytes)).IsTrue();
    }
}
