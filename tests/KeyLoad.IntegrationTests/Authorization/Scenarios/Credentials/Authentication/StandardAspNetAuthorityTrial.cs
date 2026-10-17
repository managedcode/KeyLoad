using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class StandardAspNetAuthorityTrial
{
    private const string RolesHeader = "X-KeyLoad-Roles";
    private const string PrincipalHeader = "X-KeyLoad-Principal";
    private const string ForgedRole = "Administrator";
    private const string ForgedPrincipal = "root";
    private const string UnknownCredentialSuffix = "-not-persisted";

    internal static void AddHeaders(HttpClient http)
    {
        http.DefaultRequestHeaders.Add(RolesHeader, ForgedRole);
        http.DefaultRequestHeaders.Add(PrincipalHeader, ForgedPrincipal);
    }

    internal static async Task RunAsync(HttpClient http, KeyLoadClient root, KeyLoadClient reader,
        McpOfficialClient session, DiscoveryAuthorityState state, CancellationToken token)
    {
        var before = await DiscoveryAuthorityValues.ImageAsync(root, state, token);
        var unknown = new KeyLoadClient(http, state.Identity.Secret + UnknownCredentialSuffix,
            IntegrationClientOptions.Execution());
        var absentIdentity = await unknown.GetAsync(state.Scenario.Reference, token);
        await Assert.That(absentIdentity.IsSuccess).IsFalse();
        await Assert.That(absentIdentity.Value).IsNull();
        await Assert.That(absentIdentity.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Unauthenticated));
        await SqlRf3Protocol.EqualAsync(before, await DiscoveryAuthorityValues.ImageAsync(root, state, token));
        var command = state.Scenario.Command(Guid.NewGuid());
        var deniedWrite = await reader.CommitAsync(command, token);
        await Assert.That(deniedWrite.IsSuccess).IsFalse();
        await Assert.That(deniedWrite.Value).IsNull();
        await Assert.That(deniedWrite.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await SqlRf3Protocol.EqualAsync(before, await DiscoveryAuthorityValues.ImageAsync(root, state, token));
        var officialDenied = await session.CallAsync(McpCallerTools.DocumentsCommit, command, token);
        await McpCallerAssertions.ErrorAsync(officialDenied, ErrorCode.PermissionDenied, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(officialDenied, state.Identity.Secret, McpDocumentProtocol.PrivateValue);
        await SqlRf3Protocol.EqualAsync(before, await DiscoveryAuthorityValues.ImageAsync(root, state, token));
        var changed = state.Scenario.Command(command.CommandId, McpDocumentProtocol.ConflictingJson);
        var changedDenied = await session.CallAsync(McpCallerTools.DocumentsCommit, changed, token);
        await McpCallerAssertions.ErrorAsync(changedDenied, ErrorCode.PermissionDenied, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(changedDenied, state.Identity.Secret, McpDocumentProtocol.ConflictValue);
        await SqlRf3Protocol.EqualAsync(before, await DiscoveryAuthorityValues.ImageAsync(root, state, token));
        await DiscoveryAuthorityValues.HealthyAsync(root, reader, session, state, token);
    }
}
