using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class CredentialSubjectReplacementAssertions
{
    internal static async Task<CredentialSubjectReplacementImage> ImageAsync(KeyLoadClient root,
        CredentialSubjectReplacementState state, CancellationToken token)
        => new(await DiscoveryAuthorityValues.ImageAsync(root, state.First, token),
            await DiscoveryAuthorityValues.ImageAsync(root, state.Second, token));

    internal static async Task StageAsync(KeyLoadClient root, KeyLoadClient reader, McpOfficialClient session,
        CredentialSubjectReplacementState state, bool second, CancellationToken token)
    {
        var before = await ImageAsync(root, state, token);
        var allowed = second ? state.Second : state.First;
        var denied = second ? state.First : state.Second;
        await DiscoveryAuthorityValues.HealthyAsync(root, reader, session, allowed, token);
        await SqlRf3Protocol.EqualAsync(before, await ImageAsync(root, state, token));
        var sdk = await reader.GetAsync(denied.Scenario.Reference, token);
        await Assert.That(sdk.IsSuccess).IsFalse();
        await Assert.That(sdk.Value).IsNull();
        await Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        var sdkJson = System.Text.Json.JsonSerializer.Serialize(sdk, JsonDefaults.Options);
        await Assert.That(sdkJson.Contains(McpDocumentProtocol.PrivateValue, StringComparison.Ordinal)).IsFalse();
        await Assert.That(sdkJson.Contains(state.First.Identity.Secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(sdkJson.Contains(state.Second.Identity.Secret, StringComparison.Ordinal)).IsFalse();
        await SqlRf3Protocol.EqualAsync(before, await ImageAsync(root, state, token));
        var official = await session.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(denied.Scenario.Reference), token);
        await McpCallerAssertions.ErrorAsync(official, ErrorCode.PermissionDenied, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(official, state.First.Identity.Secret, McpDocumentProtocol.PrivateValue);
        await McpCallerAssertions.DoesNotDiscloseAsync(official, state.Second.Identity.Secret, McpDocumentProtocol.PrivateValue);
        await SqlRf3Protocol.EqualAsync(before, await ImageAsync(root, state, token));
    }

    internal static async Task SeedsAsync(KeyLoadClient root, CredentialSubjectReplacementState state, CancellationToken token)
    {
        var before = await ImageAsync(root, state, token);
        var first = await McpCallerAssertions.SdkSuccessAsync(await root.CommitAsync(state.FirstSeedCommand, token));
        await SqlRf3Protocol.EqualAsync(state.FirstSeedReceipt, first);
        await SqlRf3Protocol.EqualAsync(before, await ImageAsync(root, state, token));
        var second = await McpCallerAssertions.SdkSuccessAsync(await root.CommitAsync(state.SecondSeedCommand, token));
        await SqlRf3Protocol.EqualAsync(state.SecondSeedReceipt, second);
        await SqlRf3Protocol.EqualAsync(before, await ImageAsync(root, state, token));
    }
}
