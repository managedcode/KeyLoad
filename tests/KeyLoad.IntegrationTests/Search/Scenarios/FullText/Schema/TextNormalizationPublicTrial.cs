using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.Search;

internal static class TextNormalizationPublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async caller =>
            {
                var scenario = await TextClusterScenario.CreateAsync(fixture, token);
                var originals = new List<TextNormalizationPublicScenario.Original>();
                var seeded = await SendAsync(caller, TextNormalizationPublicScenario.Seed(scenario.Partition), token);
                originals.Add(seeded);
                await TextNormalizationPublicAssertions.RequireAsync(caller, scenario, seeded.Receipt.Token, 0, token);
                var patched = await SendAsync(caller, TextNormalizationPublicScenario.Change(scenario.Partition), token);
                originals.Add(patched);
                await TextNormalizationPublicAssertions.RequireAsync(caller, scenario, patched.Receipt.Token, 1, token);
                var deleted = await SendAsync(caller, TextNormalizationPublicScenario.Delete(scenario.Partition), token);
                originals.Add(deleted);
                await TextNormalizationPublicAssertions.RequireAsync(caller, scenario, deleted.Receipt.Token, 2, token);
                var repaired = await SendAsync(caller, TextNormalizationPublicScenario.Repair(scenario.Partition), token);
                originals.Add(repaired);
                await TextNormalizationPublicAssertions.RequireAsync(caller, scenario, repaired.Receipt.Token, 3, token);
                var baseline = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.OutboxStatusAsync(scenario.Partition, token));
                foreach (var original in originals)
                {
                    await SqlClusterProtocol.EqualAsync(original.Receipt, await McpCallerAssertions.SdkSuccessAsync(
                        await caller.Sdk.CommitAsync(original.Command, token)));
                    await TextNormalizationPublicAssertions.RequireAsync(caller, scenario, repaired.Receipt.Token, 3, token);
                    await TextNormalizationPublicAssertions.OutboxAsync(caller, scenario.Partition, baseline, token);
                    await SqlClusterProtocol.EqualAsync(original.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                        await caller.Mcp.CallAsync(McpCallerTools.DocumentsCommit, original.Command, token))).Value);
                    await TextNormalizationPublicAssertions.RequireAsync(caller, scenario, repaired.Receipt.Token, 3, token);
                    await TextNormalizationPublicAssertions.OutboxAsync(caller, scenario.Partition, baseline, token);
                }
                return true;
            }, token);
    }

    private static async Task<TextNormalizationPublicScenario.Original> SendAsync(RequestCqrsClusterCallers caller,
        CommandRequest command, CancellationToken token)
    {
        var result = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(command, token));
        await Assert.That(result.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(result.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(result.PersistedCopies).IsEqualTo(3);
        await Assert.That(result.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        return new(command, result);
    }
}
