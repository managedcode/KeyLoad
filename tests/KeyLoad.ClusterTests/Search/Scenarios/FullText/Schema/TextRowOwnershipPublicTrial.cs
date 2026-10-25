using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;

namespace KeyLoad.ClusterTests.Search;

internal static class TextRowOwnershipPublicTrial
{
    internal sealed record Original(CommandRequest Command, CommitReceipt Receipt);

    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async administrator =>
            {
                var scenario = await TextClusterScenario.CreateAsync(fixture, token);
                var identity = await scenario.CreateReaderAsync(fixture, true, true, false, true, token);
                await Assert.That(identity.Principal.RestrictRows).IsTrue();
                await Assert.That(identity.Principal.OwnerId).IsEqualTo(TextClusterScenario.OwnerA);
                await Assert.That(identity.Principal.PolicyEpoch).IsEqualTo(1L);
                return await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
                    identity.Secret, async reader =>
                    {
                        await TextRowOwnershipPublicAssertions.PagesAsync(reader, scenario, 1L, token);
                        var hidden = await PublishAsync(administrator, scenario, false, token);
                        await TextRowOwnershipPublicAssertions.EmptyAsync(reader, scenario, token);
                        await TextRowOwnershipPublicAssertions.ReplayAsync(administrator, hidden, 2L, token);
                        await TextRowOwnershipPublicAssertions.EmptyAsync(reader, scenario, token);
                        var repaired = await PublishAsync(administrator, scenario, true, token);
                        await TextRowOwnershipPublicAssertions.PagesAsync(reader, scenario, 3L, token);
                        await TextRowOwnershipPublicAssertions.ReplayAsync(administrator, hidden, 2L, token);
                        await TextRowOwnershipPublicAssertions.PagesAsync(reader, scenario, 3L, token);
                        await TextRowOwnershipPublicAssertions.ReplayAsync(administrator, repaired, 3L, token);
                        await TextRowOwnershipPublicAssertions.PagesAsync(reader, scenario, 3L, token);
                        return true;
                    }, token);
            }, token);
    }

    private static async Task<Original> PublishAsync(RequestCqrsClusterCallers administrator,
        TextClusterScenario scenario, bool restore, CancellationToken token)
    {
        var owner = restore ? TextClusterScenario.OwnerA : TextClusterScenario.OwnerB;
        var command = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new PutDocument(TextClusterScenario.Collection, TextClusterScenario.FirstId,
                TextClusterScenario.Document("Київ needle needle", owner), restore ? 2L : 1L, new(owner))]);
        var receipt = restore
            ? (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await administrator.Mcp.CallAsync(
                McpCallerTools.DocumentsCommit, command, token))).Value
            : await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token));
        var original = new Original(command, receipt);
        await TextRowOwnershipPublicAssertions.ReceiptAsync(original, restore ? 3L : 2L);
        return original;
    }
}
