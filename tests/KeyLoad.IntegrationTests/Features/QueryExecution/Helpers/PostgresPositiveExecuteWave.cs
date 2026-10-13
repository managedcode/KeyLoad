using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresPositiveExecuteWave
{
    internal static async Task RunAsync(ClusterFixture fixture, PostgresRf3Selection selection,
        PostgresRf3State state, bool controls)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            await NativeAsync(fixture, state, root.Sdk, controls, deadline.Token);
            var image = await PostgresPortalOwnershipAssertions.ImageAsync(root.Sdk, state, deadline.Token);
            if (controls)
            { await PostgresRf3Cancellation.RunAsync(fixture, selection, state, root.Sdk, deadline.Token); }
            await PostgresPortalOwnershipHealthy.RunAsync(fixture, state, deadline.Token);
            await global::TUnit.Assertions.Assert.That(await PostgresPortalOwnershipAssertions.ImageAsync(root.Sdk, state, deadline.Token)).IsEqualTo(image);
            return true;
        }, deadline.Token);
    }
    private static async Task NativeAsync(ClusterFixture fixture, PostgresRf3State state,
        KeyLoadClient root, bool controls, CancellationToken token)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var wire = await PostgresRf3WireOwner.OpenAsync(fixture, token);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                _ = await PostgresRf3WireAuthentication.AuthenticateAsync(wire, state.Reader, token);
                var image = await PostgresPortalOwnershipAssertions.ImageAsync(root, state, token);
                await PostgresPositiveExecuteTrial.RunAsync(wire.Stream, state, token);
                await global::TUnit.Assertions.Assert.That(await PostgresPortalOwnershipAssertions.ImageAsync(root, state, token)).IsEqualTo(image);
                if (controls)
                { await PostgresPositiveExecutePolicy.RunAsync(fixture, state, root, wire.Stream, token); }
                await PostgresRf3WireMessage.WriteAsync(wire.Stream, PostgresPortalOwnershipProtocol.Terminate, [], token);
                await PostgresRf3TransportClosed.RequireAsync(wire.Stream, token);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
