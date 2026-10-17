using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeAnnSettledRf3Assertions
{
    internal static async Task ReplayAsync(RequestCqrsRf3Callers caller, NativeAnnMaintenanceRf3Scenario scenario,
        AnnMaintenanceRequest original, AnnMaintenanceResult expected, CancellationToken token)
    {
        var before = await ReadAsync(caller.Sdk, original, token);
        var direct = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.MaintainAnnIndexAsync(original, token));
        var official = (await McpCallerAssertions.SuccessAsync<AnnMaintenanceResult>(await caller.Mcp.CallAsync(
            NativeAnnMaintenanceRf3Scenario.Tool, original, token))).Value;
        await ExactAsync(expected, direct);
        await ExactAsync(expected, official);
        await UnchangedAsync(before, await ReadAsync(caller.Sdk, original, token));
        await NativeAnnMaintenanceRf3Assertions.CanonicalAsync(caller.Sdk, caller.Mcp, scenario, token);
    }

    internal static async Task RefuseAsync(RequestCqrsRf3Callers caller, AnnMaintenanceRequest request,
        ErrorCode expected, CancellationToken token)
    {
        var before = await ReadAsync(caller.Sdk, request, token);
        await DeniedAsync(caller, request, expected, token);
        await UnchangedAsync(before, await ReadAsync(caller.Sdk, request, token));
    }

    internal static async Task DeniedAsync(RequestCqrsRf3Callers caller, AnnMaintenanceRequest request,
        ErrorCode expected, CancellationToken token)
    {
        var result = await caller.Sdk.MaintainAnnIndexAsync(request, token);
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(expected.ToString());
        _ = await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(
            NativeAnnMaintenanceRf3Scenario.Tool, request, token), expected, dispatched: true);
    }

    internal static async Task<ProjectionBatch> ReadAsync(KeyLoadClient sdk, AnnMaintenanceRequest request, CancellationToken token)
        => await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadProjectionAsync(new(request.Consumer), token));

    internal static async Task UnchangedAsync(ProjectionBatch before, ProjectionBatch after)
    {
        await Assert.That(string.IsNullOrEmpty(before.Token)).IsFalse();
        await Assert.That(string.IsNullOrEmpty(after.Token)).IsFalse();
        // The signed read token has its own fresh expiry. All persisted state and page content remain exact.
        await ExactAsync(before with
        {
            Token = after.Token
        }, after);
    }

    internal static async Task ExactAsync<T>(T expected, T actual)
        => await Assert.That(NativeSerialization.Serialize(actual).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
}
