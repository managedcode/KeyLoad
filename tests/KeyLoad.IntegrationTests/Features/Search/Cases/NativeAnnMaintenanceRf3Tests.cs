using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class NativeAnnMaintenanceRf3Tests(ClusterFixture fixture)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActualAdminSdkAndOfficialMcpDiscoverPhysicalOwnerRejectRivalAndRestoreNativeGeneration(bool officialBuild)
    {
        fixture.RegisterNativeCoverageCase<NativeAnnMaintenanceRf3Tests>(
            nameof(ActualAdminSdkAndOfficialMcpDiscoverPhysicalOwnerRejectRivalAndRestoreNativeGeneration));
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node1, fixture.AdminKey, deadline.Token);
        var scenario = new NativeAnnMaintenanceRf3Scenario();
        await scenario.SeedAsync(sdk, deadline.Token);
        var request = await scenario.RequestAsync(sdk, mcp, deadline.Token);
        var wrong = request with { NodeId = Guid.NewGuid() };
        var denied = await sdk.MaintainAnnIndexAsync(wrong, deadline.Token);
        await Assert.That(denied.IsSuccess).IsFalse();
        await Assert.That(denied.Value).IsNull();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(ErrorCode.OwnershipLost.ToString());
        _ = await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(NativeAnnMaintenanceRf3Scenario.Tool, wrong,
            deadline.Token), ErrorCode.OwnershipLost, dispatched: true);
        await NativeAnnMaintenanceRf3Assertions.CanonicalAsync(sdk, mcp, scenario, deadline.Token);
        var built = officialBuild
            ? (await McpCallerAssertions.SuccessAsync<AnnMaintenanceResult>(await mcp.CallAsync(NativeAnnMaintenanceRf3Scenario.Tool, request, deadline.Token))).Value
            : await McpCallerAssertions.SdkSuccessAsync(await sdk.MaintainAnnIndexAsync(request, deadline.Token));
        await NativeAnnMaintenanceRf3Assertions.ResultAsync(built, request);
        await NativeAnnMaintenanceRf3Assertions.CanonicalAsync(sdk, mcp, scenario, deadline.Token);
        var restore = request with { CommandId = Guid.NewGuid(), Mode = AnnMaintenanceMode.Restore };
        var restored = officialBuild
            ? await McpCallerAssertions.SdkSuccessAsync(await sdk.MaintainAnnIndexAsync(restore, deadline.Token))
            : (await McpCallerAssertions.SuccessAsync<AnnMaintenanceResult>(await mcp.CallAsync(NativeAnnMaintenanceRf3Scenario.Tool, restore, deadline.Token))).Value;
        await NativeAnnMaintenanceRf3Assertions.ResultAsync(restored, restore);
        await Assert.That(restored.IndexSha256).IsEqualTo(built.IndexSha256);
        await Assert.That(restored.Source!.CorpusSha256).IsEqualTo(built.Source!.CorpusSha256);
        await NativeAnnMaintenanceRf3Assertions.CanonicalAsync(sdk, mcp, scenario, deadline.Token);
        await NativeAnnMaintenanceReleaseAssertions.AssertAsync(sdk, mcp, scenario, request, deadline.Token);
    }
}
