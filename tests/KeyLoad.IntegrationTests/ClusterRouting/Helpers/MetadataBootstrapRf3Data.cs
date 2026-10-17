using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class MetadataBootstrapRf3Data
{
    internal static async Task CommitAsync(MetadataBootstrapRf3State state, PartitionRef partition, string id, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(MetadataBootstrapRf3State.Collection, id, MetadataBootstrapRf3State.Json, 0L)]);
        var receipt = await MetadataBootstrapRf3Calls.SdkAsync(state, state.Fixture.AdminKey, sdk => sdk.CommitAsync(command, token));
        state.Commands.Add((command, receipt));
        await ReceiptAsync(state, command, receipt, token);
        await RequireAsync(state, token);
    }
    internal static async Task RequireAsync(MetadataBootstrapRf3State state, CancellationToken token)
    {
        foreach (var (command, receipt) in state.Commands)
        {
            var before = await MetadataBootstrapRf3Calls.SdkAsync(state, state.Fixture.AdminKey,
                sdk => sdk.OutboxStatusAsync(command.Partition, token));
            await MetadataBootstrapRf3Calls.EqualAsync(receipt, await MetadataBootstrapRf3Calls.SdkAsync(state, state.Fixture.AdminKey,
                sdk => sdk.CommitAsync(command, token)));
            await MetadataBootstrapRf3Calls.McpAsync(state, state.Fixture.AdminKey, async mcp =>
            {
                var official = await mcp.Client.InvokeKeyLoadToolAsync(McpCallerTools.DocumentsCommit,
                    new Dictionary<string, object?>(StringComparer.Ordinal) { [McpCallerProtocol.Request] = System.Text.Json.JsonSerializer.SerializeToElement(command, JsonDefaults.Options) },
                    cancellationToken: token);
                await MetadataBootstrapRf3Calls.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(official)).Value);
                await ReceiptAsync(state, command, receipt, token);
                var mutation = (PutDocument)command.Mutations[0];
                var reference = new EntityRef(command.Partition, MetadataBootstrapRf3State.Collection, mutation.Id);
                var expected = new DocumentResult(reference, 1L, MetadataBootstrapRf3State.Json, false, []);
                await MetadataBootstrapRf3Calls.EqualAsync(new DocumentReadResult(expected), await MetadataBootstrapRf3Calls.SdkAsync(state,
                    state.Fixture.AdminKey, sdk => sdk.GetAsync(reference, receipt.Token, token)));
                var reply = await mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, receipt.Token), token);
                await MetadataBootstrapRf3Calls.EqualAsync(expected, (await PublicReadAssertions.DocumentAsync(reply)).Value);
                await MetadataBootstrapRf3Calls.EqualAsync(before, await MetadataBootstrapRf3Calls.SdkAsync(state, state.Fixture.AdminKey,
                    sdk => sdk.OutboxStatusAsync(command.Partition, token)));
                return true;
            }, token);
        }
    }
    private static async Task ReceiptAsync(MetadataBootstrapRf3State state, CommandRequest command, CommitReceipt receipt, CancellationToken token)
    {
        var expected = new CommitReceipt(command.CommandId, new(state.Owner.Incarnation, command.Partition.AtomicPartitionId,
            receipt.Token.Position, 1L), [new("putDocument", MetadataBootstrapRf3State.Collection, ((PutDocument)command.Mutations[0]).Id, 1L)],
            DurabilityProfile.QuorumProcessDurable);
        await MetadataBootstrapRf3Calls.EqualAsync(expected, receipt);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        var status = await MetadataBootstrapRf3Calls.SdkAsync(state, state.Fixture.AdminKey, sdk => sdk.StatusAsync(token));
        await Assert.That(status.Incarnation).IsEqualTo(state.Owner.Incarnation);
        await Assert.That(status.Applied).IsGreaterThanOrEqualTo(receipt.Token.Position);
    }
}
