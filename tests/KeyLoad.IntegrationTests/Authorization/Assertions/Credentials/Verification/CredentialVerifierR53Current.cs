using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class CredentialVerifierR53Current
{
    internal static async Task CaptureAsync(KeyLoadClient root, McpOfficialClient administrator,
        CredentialSubjectReplacementState state, CredentialVerifierR53State verifier, CancellationToken token)
    {
        verifier.Image = await CredentialSubjectReplacementAssertions.ImageAsync(root, state, token);
        verifier.FirstOutbox = await McpCallerAssertions.SdkSuccessAsync(await root.OutboxStatusAsync(state.First.Scenario.Partition, token));
        verifier.SecondOutbox = await McpCallerAssertions.SdkSuccessAsync(await root.OutboxStatusAsync(state.Second.Scenario.Partition, token));
        await RequireAsync(root, administrator, state, verifier, token);
    }

    internal static async Task RequireAsync(KeyLoadClient root, McpOfficialClient administrator,
        CredentialSubjectReplacementState state, CredentialVerifierR53State verifier, CancellationToken token)
    {
        var image = verifier.Image ?? throw new InvalidOperationException(CredentialVerifierR53Protocol.MissingState);
        await Assert.That(verifier.FirstOutbox).IsNotNull();
        await Assert.That(verifier.SecondOutbox).IsNotNull();
        await SqlClusterProtocol.EqualAsync(image, await CredentialSubjectReplacementAssertions.ImageAsync(root, state, token));
        await DocumentAsync(administrator, state.First, token);
        await DocumentAsync(administrator, state.Second, token);
        await SqlClusterProtocol.EqualAsync(verifier.FirstOutbox, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await administrator.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.First.Scenario.Partition), token))).Value);
        await SqlClusterProtocol.EqualAsync(verifier.SecondOutbox, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await administrator.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Second.Scenario.Partition), token))).Value);
        await PlacementAsync();
        await PlacementAsync();
    }

    internal static async Task ReaderAsync(KeyLoadClient root, KeyLoadClient reader, McpOfficialClient session,
        McpOfficialClient administrator, CredentialSubjectReplacementState state,
        CredentialVerifierR53State verifier, CancellationToken token)
    {
        var own = await PublicReadAssertions.DocumentAsync(await reader.GetAsync(state.First.Scenario.Reference, token));
        await SqlClusterProtocol.EqualAsync<DocumentResult?>(DiscoveryAuthorityValues.Expected(state.First), own);
        await RequireAsync(root, administrator, state, verifier, token);
        var official = await PublicReadAssertions.RequiredDocumentAsync(await session.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(state.First.Scenario.Reference), token));
        await SqlClusterProtocol.EqualAsync(DiscoveryAuthorityValues.Expected(state.First), official.Value);
        await RequireAsync(root, administrator, state, verifier, token);
        var denied = await reader.GetAsync(state.Second.Scenario.Reference, token);
        await CredentialVerifierR53Refusals.PrivateAsync(System.Text.Json.JsonSerializer.Serialize(denied, JsonDefaults.Options), state, verifier);
        await Assert.That(denied.IsSuccess).IsFalse();
        await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.PermissionDenied,
            CredentialVerifierR53Protocol.ScopeDeniedDetail), denied.Problem);
        await Assert.That(denied.Value).IsNull();
        await RequireAsync(root, administrator, state, verifier, token);
        var refused = await session.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(state.Second.Scenario.Reference), token);
        await CredentialVerifierR53Refusals.PrivateAsync(System.Text.Json.JsonSerializer.Serialize(refused, JsonDefaults.Options), state, verifier);
        await McpCallerAssertions.ErrorAsync(refused, ErrorCode.PermissionDenied, dispatched: true);
        var expectedProblem = System.Text.Json.JsonSerializer.SerializeToElement(Errors.Problem(
            ErrorCode.PermissionDenied, CredentialVerifierR53Protocol.ScopeDeniedDetail), JsonDefaults.Options);
        var actualProblem = refused.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error);
        await Assert.That(System.Text.Json.JsonElement.DeepEquals(expectedProblem, actualProblem)).IsTrue();
        await RequireAsync(root, administrator, state, verifier, token);
    }

    private static async Task DocumentAsync(McpOfficialClient administrator, DiscoveryAuthorityState state,
        CancellationToken token)
    {
        var document = await PublicReadAssertions.RequiredDocumentAsync(await administrator.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(state.Scenario.Reference), token));
        await SqlClusterProtocol.EqualAsync(DiscoveryAuthorityValues.Expected(state), document.Value);
    }

    private static async Task PlacementAsync()
    {
    }
}
