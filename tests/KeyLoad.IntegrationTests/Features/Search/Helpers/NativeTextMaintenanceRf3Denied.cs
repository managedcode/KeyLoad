using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextMaintenanceRf3Denied
{
    private const string InvalidRequest = "The internal request has an invalid scope, key, expiry or operation.";

    internal static async Task OwnerAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        NativeTextMaintenanceRf3Scenario scenario, TextIndexMaintenanceRequest request, CancellationToken token)
    {
        var wrong = request with { NodeId = Guid.NewGuid() };
        var direct = await sdk.MaintainTextIndexAsync(wrong, token);
        await Assert.That(direct.IsSuccess).IsFalse();
        await Assert.That(direct.Value).IsNull();
        await Assert.That(direct.Problem?.ErrorCode).IsEqualTo(ErrorCode.OwnershipLost.ToString());
        await Assert.That(direct.Problem?.Detail).IsEqualTo(InvalidRequest);
        var official = await mcp.CallAsync(TextIndexMaintenanceProtocol.ToolName, wrong, token);
        _ = await McpCallerAssertions.ErrorAsync(official, ErrorCode.OwnershipLost, dispatched: true);
        await Assert.That(official.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(InvalidRequest);
        await NativeTextMaintenanceRf3Assertions.CanonicalAsync(sdk, mcp, scenario, changed: false, token);
    }
}
