using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class AnnPublicRf3Scenario
{
    private const Capability ReadCapabilities = Capability.Query | Capability.DocumentsRead | Capability.VectorSearch;

    internal static async Task RunAsync(ClusterFixture fixture, bool empty, List<Exception> failures)
    {
        using var deadline = McpCallerDeadline.Create();
        await using var admin = await RequestCqrsRf3Callers.ConnectAsync(fixture.App,
            McpCallerProtocol.Node1, fixture.AdminKey, deadline.Token);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var scenario = new NativeAnnMaintenanceRf3Scenario();
            await scenario.SeedAsync(admin.Sdk, deadline.Token);
            var pin = await scenario.RequestAsync(admin.Sdk, admin.Mcp, deadline.Token);
            var built = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.MaintainAnnIndexAsync(pin, deadline.Token));
            await NativeAnnMaintenanceRf3Assertions.ResultAsync(built, pin);
            var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
                NativeAnnMaintenanceRf3Scenario.Collection, ReadCapabilities, deadline.Token);
            await Assert.That(identity.Principal.ClusterAdministrator).IsFalse();
            await using var reader = await RequestCqrsRf3Callers.ConnectAsync(fixture.App,
                McpCallerProtocol.Node1, identity.Secret, deadline.Token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var denied = await reader.Sdk.MaintainAnnIndexAsync(pin with { CommandId = Guid.NewGuid() }, deadline.Token);
                await Assert.That(denied.IsSuccess).IsFalse();
                await Assert.That(denied.Value).IsNull();
                await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
                var before = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.StatusAsync(deadline.Token));
                await AnnPublicRf3Assertions.AllPathsAsync(reader.Sdk, reader.Mcp, scenario, pin, empty, deadline.Token);
                var after = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.StatusAsync(deadline.Token));
                await Assert.That(after.Applied).IsEqualTo(before.Applied);
                await NativeAnnMaintenanceRf3Assertions.CanonicalAsync(admin.Sdk, admin.Mcp, scenario, deadline.Token);
            }, failures);
        }, failures);
    }
}
