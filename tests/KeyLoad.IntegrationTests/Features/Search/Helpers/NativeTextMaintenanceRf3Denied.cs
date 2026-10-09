using KeyLoad.Client;
using KeyLoad.Server;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextMaintenanceRf3Denied
{
    private const string InvalidRequest = "The internal request has an invalid scope, key, expiry or operation.";

    internal static async Task<bool> OwnerAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        NativeTextMaintenanceRf3Scenario scenario, TextIndexMaintenanceRequest request, List<Exception> failures, CancellationToken token)
    {
        var failureCount = failures.Count;
        var original = await NativeTextMaintenanceRf3Reconciliation.CaptureAsync(sdk, mcp, scenario, failures, token);
        if (original is null)
        { return false; }
        var wrong = request with { NodeId = Guid.NewGuid() };
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var direct = await sdk.MaintainTextIndexAsync(wrong, token);
            await Assert.That(direct.IsSuccess).IsFalse();
            await Assert.That(direct.Value).IsNull();
            await Assert.That(direct.Problem?.ErrorCode).IsEqualTo(ErrorCode.OwnershipLost.ToString())
                .Because(NativeTextMaintenanceFailureDiagnostic.Describe(direct.Problem?.ErrorCode, direct.Problem?.Detail));
            await Assert.That(direct.Problem?.Detail).IsEqualTo(InvalidRequest);
        }, failures);
        await original.RequireAsync(sdk, mcp, scenario, failures, token);
        if (failures.Count != failureCount)
        { return false; }
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var official = await mcp.CallAsync(TextIndexMaintenanceProtocol.ToolName, wrong, token);
            _ = await McpCallerAssertions.ErrorAsync(official, ErrorCode.OwnershipLost, dispatched: true);
            await Assert.That(official.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
                .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(InvalidRequest);
        }, failures);
        await original.RequireAsync(sdk, mcp, scenario, failures, token);
        if (failures.Count != failureCount)
        { return false; }
        await ServerFailureObserver.ObserveAsync(() => NativeTextMaintenanceRf3Assertions.CanonicalAsync(
            sdk, mcp, scenario, changed: false, token), failures);
        return failures.Count == failureCount;
    }
}
