using System.Net;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class CredentialAuthorityFlow
{
    private const string MissingRepairResult = "Original acknowledged credential repair is absent.";

    internal static async Task RunAsync(ClusterFixture fixture, CredentialAuthorityState state, bool expire, CancellationToken token)
    {
        await CredentialAuthorityClients.RunAsync(fixture, state, expire, changeCredential: true, token);
        await CredentialAuthorityCold.RunAsync(fixture, state, expire, token);
    }

    internal static async Task WarmAsync(ClusterFixture fixture, KeyLoadClient root, KeyLoadClient reader,
        McpOfficialClient session, CredentialAuthorityState state, bool expire, CancellationToken token)
    {
        var identity = state.Document.Identity;
        var unusable = expire ? identity.Credential with
        { ExpiresAt = TimeProvider.System.GetUtcNow() - McpCallerProtocol.ExpiredOffset }
            : identity.Credential with { Revoked = true };
        var changed = await McpCallerAssertions.SdkSuccessAsync(await root.ConfigureApiKeyAsync(Guid.NewGuid(), unusable, token));
        await Assert.That(changed).IsTrue();
        await RefusedAsync(fixture, root, reader, session, state, token);
        await Assert.That((await McpCallerAssertions.SdkSuccessAsync(await root.GetAsync(state.Document.Scenario.Reference, token)))!.Revision)
            .IsEqualTo(McpCallerProtocol.FirstRevision);
        state.OriginalRepairResult = await McpCallerAssertions.SdkSuccessAsync(await root.ConfigureApiKeyAsync(
            state.RepairCommandId, identity.Credential, token));
        await SqlRf3Protocol.EqualAsync<bool?>(true, state.OriginalRepairResult);
        await DiscoveryAuthorityValues.HealthyAsync(root, reader, session, state.Document, token);
        await ReplayAsync(root, state, token);
    }

    private static async Task RefusedAsync(ClusterFixture fixture, KeyLoadClient root, KeyLoadClient reader,
        McpOfficialClient session, CredentialAuthorityState state, CancellationToken token)
    {
        var document = state.Document;
        var before = await DiscoveryAuthorityValues.ImageAsync(root, document, token);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => session.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(document.Scenario.Reference), token));
        await Assert.That(error).IsNotNull();
        await Assert.That(error?.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(error?.Message.Contains(document.Identity.Secret, StringComparison.Ordinal) == true).IsFalse();
        await Assert.That(error?.Message.Contains(McpDocumentProtocol.PrivateValue, StringComparison.Ordinal) == true).IsFalse();
        await McpUnauthorizedProbe.VerifyAsync(fixture, McpCallerProtocol.Node2, document.Identity.Secret, token);
        var rejected = await reader.GetAsync(document.Scenario.Reference, token);
        await Assert.That(rejected.IsSuccess).IsFalse();
        await Assert.That(rejected.Value).IsNull();
        await Assert.That(rejected.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Unauthenticated));
        var serialized = JsonSerializer.Serialize(rejected, JsonDefaults.Options);
        await Assert.That(serialized.Contains(document.Identity.Secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(serialized.Contains(McpDocumentProtocol.PrivateValue, StringComparison.Ordinal)).IsFalse();
        await SqlRf3Protocol.EqualAsync(before, await DiscoveryAuthorityValues.ImageAsync(root, document, token));
    }

    internal static async Task ReplayAsync(KeyLoadClient root, CredentialAuthorityState state, CancellationToken token)
    {
        if (state.OriginalRepairResult is not true)
        { throw new InvalidOperationException(MissingRepairResult); }
        var original = state.OriginalRepairResult.Value;
        var before = await DiscoveryAuthorityValues.ImageAsync(root, state.Document, token);
        var replay = await McpCallerAssertions.SdkSuccessAsync(await root.ConfigureApiKeyAsync(
            state.RepairCommandId, state.Document.Identity.Credential, token));
        await Assert.That(replay).IsEqualTo(original);
        await SqlRf3Protocol.EqualAsync(before, await DiscoveryAuthorityValues.ImageAsync(root, state.Document, token));
    }
}
