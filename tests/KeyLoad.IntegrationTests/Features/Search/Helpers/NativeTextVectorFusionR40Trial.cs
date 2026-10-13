using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextVectorFusionR40Trial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async admin =>
            {
                var current = await NativeTextVectorFusionR40Seed.CreateAsync(fixture, admin, token);
                return await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, current.Reader.Secret,
                    async reader =>
                    {
                        await NativeTextVectorFusionR40Pages.MatrixAsync(admin, reader, current, 1L, true, token);
                        await NativeTextVectorFusionR40Replay.AllAsync(admin, reader, current, 1L, token);
                        foreach (var epoch in new[] { 2L, 3L, 4L })
                        {
                            await NativeTextVectorFusionR40Policy.ApplyAsync(admin, current, epoch, token);
                            await NativeTextVectorFusionR40Pages.MatrixAsync(admin, reader, current, epoch, true, token);
                            await NativeTextVectorFusionR40Replay.AllAsync(admin, reader, current, epoch, token);
                        }
                        return current;
                    }, token);
            }, token);
        var beforeFirst = await StatusAsync(fixture, token);
        await NativeTextProtectedPublicColdRestart.RunAsync(fixture, token);
        await ContinueAsync(fixture, state, beforeFirst, true, token);
        var beforeSecond = await StatusAsync(fixture, token);
        await NativeTextProtectedPublicColdRestart.RunAsync(fixture, token);
        await ContinueAsync(fixture, state, beforeSecond, false, token);
    }

    private static Task<NodeStatus> StatusAsync(ClusterFixture fixture, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async admin => await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.StatusAsync(token)), token);

    private static Task<bool> ContinueAsync(ClusterFixture fixture, NativeTextVectorFusionR40State state,
        NodeStatus beforeCold, bool repair, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            admin => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, state.Reader.Secret,
                async reader =>
                {
                    var current = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.StatusAsync(token));
                    await Assert.That(current.NodeId).IsEqualTo(beforeCold.NodeId);
                    await Assert.That(current.Incarnation).IsEqualTo(beforeCold.Incarnation);
                    await Assert.That(current.Applied).IsGreaterThanOrEqualTo(beforeCold.Applied);
                    await Assert.That(current.ReadGeneration).IsGreaterThanOrEqualTo(beforeCold.ReadGeneration);
                    if (repair)
                    {
                        await NativeTextVectorFusionR40Pages.MatrixAsync(admin, reader, state, 4L, true, token);
                        await NativeTextVectorFusionR40Replay.AllAsync(admin, reader, state, 4L, token);
                        await NativeTextVectorFusionR40Policy.ApplyAsync(admin, state, 5L, token);
                    }
                    await NativeTextVectorFusionR40Pages.MatrixAsync(admin, reader, state, 5L, true, token);
                    await NativeTextVectorFusionR40Replay.AllAsync(admin, reader, state, 5L, token);
                    return true;
                }, token), token);
}
