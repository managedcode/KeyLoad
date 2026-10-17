using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextPartitionGenerationPublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var prepared = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async caller =>
            {
                var states = await NativeTextPartitionGenerationPublicSeed.CreateAsync(caller, token);
                await VerifyAsync(caller, states, token);
                var status = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
                return (States: states, Status: status);
            }, token);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await NativeTextProtectedPublicColdRestart.RunAsync(fixture, token);
            _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
                fixture.AdminKey, async caller =>
                {
                    var status = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
                    await Assert.That(status.NodeId).IsEqualTo(prepared.Status.NodeId);
                    await Assert.That(status.Incarnation).IsEqualTo(prepared.Status.Incarnation);
                    await Assert.That(status.Applied).IsGreaterThanOrEqualTo(prepared.Status.Applied);
                    await Assert.That(status.ReadGeneration).IsGreaterThanOrEqualTo(prepared.Status.ReadGeneration);
                    await VerifyAsync(caller, prepared.States, token);
                    return true;
                }, token);
        }
    }

    private static async Task VerifyAsync(RequestCqrsRf3Callers caller,
        NativeTextPartitionGenerationPublicState[] states, CancellationToken token)
    {
        await NativeTextPartitionGenerationPublicAssertions.ControlsAsync(caller, states, token);
        await NativeTextPartitionGenerationPublicAssertions.RefuseAsync(caller, states[0], states[1], states, token);
        await NativeTextPartitionGenerationPublicAssertions.RefuseAsync(caller, states[1], states[0], states, token);
        foreach (var state in states)
        {
            await NativeTextPartitionGenerationPublicAssertions.ReplayAsync(caller, state, states, token);
        }
    }
}
