using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextNormalizationPublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async caller =>
            {
                var scenario = await NativeTextClusterScenario.CreateAsync(fixture, token);
                var originals = new List<NativeTextNormalizationPublicScenario.Original>();
                var seeded = await SendAsync(caller, NativeTextNormalizationPublicScenario.Seed(scenario.Partition), token);
                originals.Add(seeded);
                await NativeTextNormalizationPublicAssertions.RequireAsync(caller, scenario, seeded.Receipt.Token, 0, token);
                var patched = await SendAsync(caller, NativeTextNormalizationPublicScenario.Change(scenario.Partition), token);
                originals.Add(patched);
                await NativeTextNormalizationPublicAssertions.RequireAsync(caller, scenario, patched.Receipt.Token, 1, token);
                var deleted = await SendAsync(caller, NativeTextNormalizationPublicScenario.Delete(scenario.Partition), token);
                originals.Add(deleted);
                await NativeTextNormalizationPublicAssertions.RequireAsync(caller, scenario, deleted.Receipt.Token, 2, token);
                var repaired = await SendAsync(caller, NativeTextNormalizationPublicScenario.Repair(scenario.Partition), token);
                originals.Add(repaired);
                await NativeTextNormalizationPublicAssertions.RequireAsync(caller, scenario, repaired.Receipt.Token, 3, token);
                var baseline = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.OutboxStatusAsync(scenario.Partition, token));
                foreach (var original in originals)
                {
                    await SqlClusterProtocol.EqualAsync(original.Receipt, await McpCallerAssertions.SdkSuccessAsync(
                        await caller.Sdk.CommitAsync(original.Command, token)));
                    await NativeTextNormalizationPublicAssertions.RequireAsync(caller, scenario, repaired.Receipt.Token, 3, token);
                    await NativeTextNormalizationPublicAssertions.OutboxAsync(caller, scenario.Partition, baseline, token);
                    await SqlClusterProtocol.EqualAsync(original.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                        await caller.Mcp.CallAsync(McpCallerTools.DocumentsCommit, original.Command, token))).Value);
                    await NativeTextNormalizationPublicAssertions.RequireAsync(caller, scenario, repaired.Receipt.Token, 3, token);
                    await NativeTextNormalizationPublicAssertions.OutboxAsync(caller, scenario.Partition, baseline, token);
                }
                return true;
            }, token);
    }

    private static async Task<NativeTextNormalizationPublicScenario.Original> SendAsync(RequestCqrsClusterCallers caller,
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
