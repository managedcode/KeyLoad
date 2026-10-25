using System.Net;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.DocumentStorage;
using KeyLoad.ClusterTests.QueryExecution;
using ManagedCode.Communication;

namespace KeyLoad.ClusterTests.Authorization;

internal static class AdversarialAuthorityRefusals
{
    internal static async Task ForeignAsync(KeyLoadClient root, KeyLoadClient reader,
        McpOfficialClient session, AdversarialAuthorityState state, CancellationToken token)
    {
        var before = await AdversarialAuthorityAssertions.ImageAsync(root, state, token);
        var request = new GetDocumentRequest(state.Foreign.Scenario.Reference);
        var denied = await session.CallAsync(McpCallerTools.DocumentsGet, request, token);
        _ = (await McpCallerAssertions.ErrorAsync(denied, ErrorCode.PermissionDenied, dispatched: true)).Value;
        await McpCallerAssertions.DoesNotDiscloseAsync(denied, state.Identity.Secret, McpDocumentProtocol.PrivateValue);
        await SdkAsync(await reader.GetAsync(state.Foreign.Scenario.Reference, token), state, ErrorCode.PermissionDenied);
        var forged = McpOfficialClient.Arguments(request);
        forged.Add(McpCallerProtocol.PrincipalId, state.Identity.Principal.Id);
        forged.Add(McpCallerProtocol.Roles, new[] { McpCallerProtocol.AdministratorRole });
        var rejected = await session.Client.InvokeKeyLoadToolAsync(McpCallerTools.DocumentsGet, forged,
            cancellationToken: token);
        _ = (await McpCallerAssertions.ErrorAsync(rejected, ErrorCode.Validation, dispatched: false)).Value;
        await McpCallerAssertions.DoesNotDiscloseAsync(rejected, state.Identity.Secret, McpDocumentProtocol.PrivateValue);
        await SqlClusterProtocol.EqualAsync(before, await AdversarialAuthorityAssertions.ImageAsync(root, state, token));
    }

    internal static async Task OwnedAsync(ClusterFixture fixture, KeyLoadClient root, KeyLoadClient reader,
        McpOfficialClient session, AdversarialAuthorityState state, ErrorCode expected, CancellationToken token)
    {
        var before = await AdversarialAuthorityAssertions.ImageAsync(root, state, token);
        await SdkAsync(await reader.GetAsync(state.Owned.Scenario.Reference, token), state, expected);
        if (expected == ErrorCode.Unauthenticated)
        {
            await RevokedAsync(fixture, session, state, token);
        }
        else
        {
            var denied = await session.CallAsync(McpCallerTools.DocumentsGet,
                new GetDocumentRequest(state.Owned.Scenario.Reference), token);
            _ = (await McpCallerAssertions.ErrorAsync(denied, expected, dispatched: true)).Value;
            await McpCallerAssertions.DoesNotDiscloseAsync(denied, state.Identity.Secret, McpDocumentProtocol.PrivateValue);
        }
        await SqlClusterProtocol.EqualAsync(before, await AdversarialAuthorityAssertions.ImageAsync(root, state, token));
    }

    private static async Task RevokedAsync(ClusterFixture fixture, McpOfficialClient session,
        AdversarialAuthorityState state, CancellationToken token)
    {
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => session.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(state.Owned.Scenario.Reference), token));
        await Assert.That(error).IsNotNull();
        await Assert.That(error!.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(error.Message.Contains(state.Identity.Secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(error.Message.Contains(McpDocumentProtocol.PrivateValue, StringComparison.Ordinal)).IsFalse();
        await McpUnauthorizedProbe.VerifyAsync(fixture, McpCallerProtocol.Node3, state.Identity.Secret, token);
    }

    private static async Task SdkAsync(Result<DocumentReadResult> result, AdversarialAuthorityState state, ErrorCode expected)
    {
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(expected.ToString());
        var safe = JsonSerializer.Serialize(result, JsonDefaults.Options);
        await Assert.That(safe.Contains(state.Identity.Secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(safe.Contains(McpDocumentProtocol.PrivateValue, StringComparison.Ordinal)).IsFalse();
    }
}
