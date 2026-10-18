using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextRequestRefusalPublicTrial
{
    internal const string Id = "query-validation";
    internal const string Term = "boundaryhorizon";
    internal sealed record Original(CommandRequest Command, CommitReceipt Receipt);

    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async administrator =>
            {
                var scenario = await NativeTextClusterScenario.CreateAsync(fixture, token);
                var identity = await scenario.CreateReaderAsync(fixture, true, true, false, true, token);
                var original = await PublishAsync(administrator, scenario, token);
                var baseline = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
                return await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
                    identity.Secret, async reader =>
                    {
                        var healthy = scenario.Text(Term);
                        await NativeTextRequestRefusalPublicAssertions.HealthyAsync(reader, scenario, token);
                        foreach (var refused in new[] { healthy with { TextField = null },
                            healthy with { Limit = 0 }, healthy with { FusionConstant = 0 } })
                        {
                            await NativeTextRequestRefusalPublicAssertions.RefusedAsync(reader, refused, identity.Secret, token);
                            await NativeTextRequestRefusalPublicAssertions.HealthyAsync(reader, scenario, token);
                            await NativeTextRequestRefusalPublicAssertions.OutboxAsync(administrator, scenario, baseline, token);
                            await NativeTextRequestRefusalPublicAssertions.ReplayAsync(administrator, original, token);
                            await NativeTextRequestRefusalPublicAssertions.HealthyAsync(reader, scenario, token);
                            await NativeTextRequestRefusalPublicAssertions.OutboxAsync(administrator, scenario, baseline, token);
                        }
                        return true;
                    }, token);
            }, token);
    }

    private static async Task<Original> PublishAsync(RequestCqrsClusterCallers administrator,
        NativeTextClusterScenario scenario, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new PutDocument(NativeTextClusterScenario.Collection, Id,
                NativeTextClusterScenario.Document(Term, NativeTextClusterScenario.OwnerA), 0L, new(NativeTextClusterScenario.OwnerA))]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token));
        return new(command, receipt);
    }
}
