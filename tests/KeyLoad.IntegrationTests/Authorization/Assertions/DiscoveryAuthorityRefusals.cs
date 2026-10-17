using System.Net;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class DiscoveryAuthorityRefusals
{
    private const string UnauthenticatedProblemType = "urn:keyload:error:Unauthenticated";
    private const string UnavailableCredentialDetail = "The credential is unavailable or expired.";
    internal static async Task RestrictedAsync(KeyLoadClient root, KeyLoadClient reader, McpOfficialClient session,
        DiscoveryAuthorityState state, Guid firstRequest, CancellationToken token)
    {
        var before = await DiscoveryAuthorityValues.ImageAsync(root, state, token);
        var denied = await session.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(state.Scenario.Reference), token);
        var deniedId = await McpCallerAssertions.ErrorAsync(denied, ErrorCode.PermissionDenied, dispatched: true);
        await Assert.That(deniedId).IsNotEqualTo(firstRequest);
        await McpCallerAssertions.DoesNotDiscloseAsync(denied, state.Identity.Secret, McpDocumentProtocol.PrivateValue);
        var sdkDenied = await reader.GetAsync(state.Scenario.Reference, token);
        await Assert.That(sdkDenied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await SdkAsync(sdkDenied, state, ErrorCode.PermissionDenied);
        await SqlRf3Protocol.EqualAsync(before, await DiscoveryAuthorityValues.ImageAsync(root, state, token));
    }

    internal static async Task UnauthenticatedAsync(ClusterFixture fixture, KeyLoadClient root, KeyLoadClient reader,
        McpOfficialClient session, DiscoveryAuthorityState state, CancellationToken token)
    {
        var before = await DiscoveryAuthorityValues.ImageAsync(root, state, token);
        var sdk = await reader.GetAsync(state.Scenario.Reference, token);
        await SdkAsync(sdk, state, ErrorCode.Unauthenticated);
        await SqlRf3Protocol.EqualAsync(new ManagedCode.Communication.Problem
        {
            Type = UnauthenticatedProblemType,
            Title = nameof(ErrorCode.Unauthenticated),
            StatusCode = (int)HttpStatusCode.Unauthorized,
            Detail = UnavailableCredentialDetail,
            ErrorCode = nameof(ErrorCode.Unauthenticated)
        }, sdk.Problem);
        await SqlRf3Protocol.EqualAsync(before, await DiscoveryAuthorityValues.ImageAsync(root, state, token));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => session.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(state.Scenario.Reference), token));
        await Assert.That(error).IsNotNull();
        await Assert.That(error?.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(error?.Message.Contains(state.Identity.Secret, StringComparison.Ordinal) == true).IsFalse();
        await Assert.That(error?.Message.Contains(McpDocumentProtocol.PrivateValue, StringComparison.Ordinal) == true).IsFalse();
        await SqlRf3Protocol.EqualAsync(before, await DiscoveryAuthorityValues.ImageAsync(root, state, token));
        await McpUnauthorizedProbe.VerifyAsync(fixture, McpCallerProtocol.Node2, state.Identity.Secret, token);
        await SqlRf3Protocol.EqualAsync(before, await DiscoveryAuthorityValues.ImageAsync(root, state, token));
    }

    private static async Task SdkAsync(ManagedCode.Communication.Result<DocumentReadResult> result,
        DiscoveryAuthorityState state, ErrorCode expected)
    {
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(expected.ToString());
        var serialized = JsonSerializer.Serialize(result, JsonDefaults.Options);
        await Assert.That(serialized.Contains(state.Identity.Secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(serialized.Contains(McpDocumentProtocol.PrivateValue, StringComparison.Ordinal)).IsFalse();
    }
}
