using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using static KeyLoad.IntegrationTests.Features.ClusterReplication.DocumentSessionReadRf3Protocol;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class DocumentSessionReadRf3Authorization
{
    internal static async Task VerifyAsync(KeyLoadClient admin, KeyLoadClient sdk, McpOfficialClient mcp,
        EntityRef reference, CommitToken minimum, McpPersistedIdentity identity, CancellationToken token)
    {
        var revoked = identity.Principal with { Grants = [], PolicyEpoch = identity.Principal.PolicyEpoch + First };
        await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigurePrincipalAsync(Guid.NewGuid(), revoked, token));
        var denied = await sdk.GetAsync(reference, minimum, token);
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        var official = await mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, minimum), token);
        await McpCallerAssertions.ErrorAsync(official, ErrorCode.PermissionDenied, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(official, identity.Secret, FirstJson);
        var restored = identity.Principal with { PolicyEpoch = revoked.PolicyEpoch + First };
        await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigurePrincipalAsync(Guid.NewGuid(), restored, token));
        await DocumentSessionReadRf3Assertions.HealthyAsync(sdk, mcp, reference, minimum, FirstJson, First, token);
    }
}
