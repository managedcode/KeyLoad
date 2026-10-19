using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class TextRequestRefusalPublicTrial
{
    internal const string Id = "query-validation";
    internal const string Term = "boundaryhorizon";
    internal sealed record Original(CommandRequest Command, CommitReceipt Receipt);

    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async administrator =>
            {
                var scenario = await TextClusterScenario.CreateAsync(fixture, token);
                var identity = await scenario.CreateReaderAsync(fixture, true, true, false, true, token);
                var original = await PublishAsync(administrator, scenario, token);
                var baseline = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
                return await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
                    identity.Secret, async reader =>
                    {
                        var healthy = scenario.Text(Term);
                        await TextRequestRefusalPublicAssertions.HealthyAsync(reader, scenario, token);
                        foreach (var refused in new[] { healthy with { TextField = null },
                            healthy with { Limit = 0 }, healthy with { FusionConstant = 0 } })
                        {
                            await TextRequestRefusalPublicAssertions.RefusedAsync(reader, refused, identity.Secret, token);
                            await TextRequestRefusalPublicAssertions.HealthyAsync(reader, scenario, token);
                            await TextRequestRefusalPublicAssertions.OutboxAsync(administrator, scenario, baseline, token);
                            await TextRequestRefusalPublicAssertions.ReplayAsync(administrator, original, token);
                            await TextRequestRefusalPublicAssertions.HealthyAsync(reader, scenario, token);
                            await TextRequestRefusalPublicAssertions.OutboxAsync(administrator, scenario, baseline, token);
                        }
                        return true;
                    }, token);
            }, token);
    }

    private static async Task<Original> PublishAsync(RequestCqrsClusterCallers administrator,
        TextClusterScenario scenario, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new PutDocument(TextClusterScenario.Collection, Id,
                TextClusterScenario.Document(Term, TextClusterScenario.OwnerA), 0L, new(TextClusterScenario.OwnerA))]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token));
        return new(command, receipt);
    }
}
