using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextRowOwnershipPublicTrial
{
    internal sealed record Original(CommandRequest Command, CommitReceipt Receipt);

    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async administrator =>
            {
                var scenario = await NativeTextRf3Scenario.CreateAsync(fixture, token);
                var identity = await scenario.CreateReaderAsync(fixture, true, true, false, true, token);
                await Assert.That(identity.Principal.RestrictRows).IsTrue();
                await Assert.That(identity.Principal.OwnerId).IsEqualTo(NativeTextRf3Scenario.OwnerA);
                await Assert.That(identity.Principal.PolicyEpoch).IsEqualTo(1L);
                return await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
                    identity.Secret, async reader =>
                    {
                        await NativeTextRowOwnershipPublicAssertions.PagesAsync(reader, scenario, 1L, token);
                        var hidden = await PublishAsync(administrator, scenario, false, token);
                        await NativeTextRowOwnershipPublicAssertions.EmptyAsync(reader, scenario, token);
                        await NativeTextRowOwnershipPublicAssertions.ReplayAsync(administrator, hidden, 2L, token);
                        await NativeTextRowOwnershipPublicAssertions.EmptyAsync(reader, scenario, token);
                        var repaired = await PublishAsync(administrator, scenario, true, token);
                        await NativeTextRowOwnershipPublicAssertions.PagesAsync(reader, scenario, 3L, token);
                        await NativeTextRowOwnershipPublicAssertions.ReplayAsync(administrator, hidden, 2L, token);
                        await NativeTextRowOwnershipPublicAssertions.PagesAsync(reader, scenario, 3L, token);
                        await NativeTextRowOwnershipPublicAssertions.ReplayAsync(administrator, repaired, 3L, token);
                        await NativeTextRowOwnershipPublicAssertions.PagesAsync(reader, scenario, 3L, token);
                        return true;
                    }, token);
            }, token);
    }

    private static async Task<Original> PublishAsync(RequestCqrsRf3Callers administrator,
        NativeTextRf3Scenario scenario, bool restore, CancellationToken token)
    {
        var owner = restore ? NativeTextRf3Scenario.OwnerA : NativeTextRf3Scenario.OwnerB;
        var command = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new PutDocument(NativeTextRf3Scenario.Collection, NativeTextRf3Scenario.FirstId,
                NativeTextRf3Scenario.Document("Київ needle needle", owner), restore ? 2L : 1L, new(owner))]);
        var receipt = restore
            ? (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await administrator.Mcp.CallAsync(
                McpCallerTools.DocumentsCommit, command, token))).Value
            : await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token));
        var original = new Original(command, receipt);
        await NativeTextRowOwnershipPublicAssertions.ReceiptAsync(original, restore ? 3L : 2L);
        return original;
    }
}
