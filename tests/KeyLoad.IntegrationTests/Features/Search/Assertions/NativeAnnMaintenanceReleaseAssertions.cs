using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeAnnMaintenanceReleaseAssertions
{
    private const int EmptyCount = 0;
    private const string GuidFormat = "N";
    private const string FreshConsumerPrefix = "ann-fresh-";

    internal static async Task AssertAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        NativeAnnMaintenanceRf3Scenario scenario, AnnMaintenanceRequest request, CancellationToken token)
    {
        var release = request with { CommandId = Guid.NewGuid(), Mode = AnnMaintenanceMode.Release };
        var result = (await McpCallerAssertions.SuccessAsync<AnnMaintenanceResult>(await mcp.CallAsync(
            NativeAnnMaintenanceRf3Scenario.Tool, release, token))).Value;
        await Assert.That(result.CommandId).IsEqualTo(release.CommandId);
        await Assert.That(result.Consumer).IsEqualTo(release.Consumer);
        await Assert.That(result.IndexGeneration).IsEqualTo(release.IndexGeneration);
        await Assert.That(result.Phase).IsEqualTo(AnnMaintenancePhase.Completed);
        await Assert.That(result.Count).IsEqualTo(EmptyCount);
        await Assert.That(result.Source).IsNull();
        await Assert.That(result.IndexSha256).IsNull();
        await Assert.That(result.Checkpoint).IsNull();
        var old = request with { CommandId = Guid.NewGuid(), Mode = AnnMaintenanceMode.Restore };
        var denied = await sdk.MaintainAnnIndexAsync(old, token);
        await Assert.That(denied.IsSuccess).IsFalse();
        await Assert.That(denied.Value).IsNull();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(ErrorCode.Conflict.ToString());
        await NativeAnnMaintenanceRf3Assertions.CanonicalAsync(sdk, mcp, scenario, token);
        var fresh = request with
        {
            CommandId = Guid.NewGuid(),
            Consumer = request.Consumer with
            { Name = FreshConsumerPrefix + Guid.NewGuid().ToString(GuidFormat) },
            Mode = AnnMaintenanceMode.Build
        };
        var healthy = await McpCallerAssertions.SdkSuccessAsync(await sdk.MaintainAnnIndexAsync(fresh, token));
        await NativeAnnMaintenanceRf3Assertions.ResultAsync(healthy, fresh);
        await NativeAnnMaintenanceRf3Assertions.CanonicalAsync(sdk, mcp, scenario, token);
    }
}
