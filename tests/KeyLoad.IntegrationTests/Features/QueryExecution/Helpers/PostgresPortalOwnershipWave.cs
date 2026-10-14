using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresPortalOwnershipWave
{
    internal static async Task RunAsync(ClusterFixture fixture, PostgresRf3State state, bool policyControls)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            await NativeAsync(fixture, state, root.Sdk, policyControls, deadline.Token);
            var image = await PostgresPortalOwnershipAssertions.ImageAsync(root.Sdk, state, deadline.Token);
            await PostgresPortalOwnershipHealthy.RunAsync(fixture, state, deadline.Token);
            await global::TUnit.Assertions.Assert.That(await PostgresPortalOwnershipAssertions.ImageAsync(
                root.Sdk, state, deadline.Token)).IsEqualTo(image);
            return true;
        }, deadline.Token);
    }

    private static async Task NativeAsync(ClusterFixture fixture, PostgresRf3State state,
        KeyLoadClient root, bool policyControls, CancellationToken token)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var wire = await PostgresRf3WireOwner.OpenAsync(fixture, token);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                _ = await PostgresRf3WireAuthentication.AuthenticateAsync(wire, state.Reader, token);
                Func<Stream, PostgresRf3State, CancellationToken, Task>[] trials =
                [
                    PostgresPortalOwnershipClose.RunAsync, PostgresPortalOwnershipClose.CapacityAsync,
                    PostgresPortalOwnershipUnnamed.ReplaceAsync, PostgresPortalOwnershipUnnamed.SimpleAsync
                ];
                foreach (var trial in trials)
                {
                    var image = await PostgresPortalOwnershipAssertions.ImageAsync(root, state, token);
                    await trial(wire.Stream, state, token);
                    await global::TUnit.Assertions.Assert.That(await PostgresPortalOwnershipAssertions.ImageAsync(root, state, token)).IsEqualTo(image);
                }
                var conserved = await PostgresPortalOwnershipAssertions.ImageAsync(root, state, token);
                await PostgresPortalMalformedPublication.RunAsync(wire.Stream, state, async () =>
                    await global::TUnit.Assertions.Assert.That(await PostgresPortalOwnershipAssertions.ImageAsync(
                        root, state, token)).IsEqualTo(conserved), token);
                if (policyControls)
                { await PostgresPortalOwnershipPolicy.RunAsync(fixture, state, root, wire.Stream, token); }
                await PostgresRf3WireMessage.WriteAsync(wire.Stream, PostgresPortalOwnershipProtocol.Terminate, [], token);
                await PostgresRf3TransportClosed.RequireAsync(wire.Stream, token);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
