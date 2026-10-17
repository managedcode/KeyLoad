using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class CredentialVerifierR53Current
{
    internal static async Task CaptureAsync(KeyLoadClient root, McpOfficialClient administrator,
        CredentialSubjectReplacementState state, CredentialVerifierR53State verifier, CancellationToken token)
    {
        verifier.Image = await CredentialSubjectReplacementAssertions.ImageAsync(root, state, token);
        verifier.FirstOutbox = await McpCallerAssertions.SdkSuccessAsync(await root.OutboxStatusAsync(state.First.Scenario.Partition, token));
        verifier.SecondOutbox = await McpCallerAssertions.SdkSuccessAsync(await root.OutboxStatusAsync(state.Second.Scenario.Partition, token));
        verifier.FirstPlacement = await McpCallerAssertions.SdkSuccessAsync(await root.ReadAtomicPartitionPlacementAsync(
            new(CredentialVerifierR53Protocol.PlacementSchema, state.First.Scenario.Partition), token));
        verifier.SecondPlacement = await McpCallerAssertions.SdkSuccessAsync(await root.ReadAtomicPartitionPlacementAsync(
            new(CredentialVerifierR53Protocol.PlacementSchema, state.Second.Scenario.Partition), token));
        await RequireAsync(root, administrator, state, verifier, token);
    }

    internal static async Task RequireAsync(KeyLoadClient root, McpOfficialClient administrator,
        CredentialSubjectReplacementState state, CredentialVerifierR53State verifier, CancellationToken token)
    {
        var image = verifier.Image ?? throw new InvalidOperationException(CredentialVerifierR53Protocol.MissingState);
        await Assert.That(verifier.FirstOutbox).IsNotNull();
        await Assert.That(verifier.SecondOutbox).IsNotNull();
        await SqlRf3Protocol.EqualAsync(image, await CredentialSubjectReplacementAssertions.ImageAsync(root, state, token));
        await DocumentAsync(administrator, state.First, token);
        await DocumentAsync(administrator, state.Second, token);
        await SqlRf3Protocol.EqualAsync(verifier.FirstOutbox, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await administrator.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.First.Scenario.Partition), token))).Value);
        await SqlRf3Protocol.EqualAsync(verifier.SecondOutbox, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await administrator.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Second.Scenario.Partition), token))).Value);
        await PlacementAsync(root, administrator, state.First.Scenario.Partition, verifier.FirstPlacement, token);
        await PlacementAsync(root, administrator, state.Second.Scenario.Partition, verifier.SecondPlacement, token);
    }

    internal static async Task ReaderAsync(KeyLoadClient root, KeyLoadClient reader, McpOfficialClient session,
        McpOfficialClient administrator, CredentialSubjectReplacementState state,
        CredentialVerifierR53State verifier, CancellationToken token)
    {
        var own = await PublicReadAssertions.DocumentAsync(await reader.GetAsync(state.First.Scenario.Reference, token));
        await SqlRf3Protocol.EqualAsync<DocumentResult?>(DiscoveryAuthorityValues.Expected(state.First), own);
        await RequireAsync(root, administrator, state, verifier, token);
        var official = await PublicReadAssertions.RequiredDocumentAsync(await session.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(state.First.Scenario.Reference), token));
        await SqlRf3Protocol.EqualAsync(DiscoveryAuthorityValues.Expected(state.First), official.Value);
        await RequireAsync(root, administrator, state, verifier, token);
        var denied = await reader.GetAsync(state.Second.Scenario.Reference, token);
        await CredentialVerifierR53Refusals.PrivateAsync(System.Text.Json.JsonSerializer.Serialize(denied, JsonDefaults.Options), state, verifier);
        await Assert.That(denied.IsSuccess).IsFalse();
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.PermissionDenied,
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
        await SqlRf3Protocol.EqualAsync(DiscoveryAuthorityValues.Expected(state), document.Value);
    }

    private static async Task PlacementAsync(KeyLoadClient root, McpOfficialClient administrator, PartitionRef partition,
        AtomicPartitionPlacementResolution? expected, CancellationToken token)
    {
        var request = new AtomicPartitionPlacementReadRequest(CredentialVerifierR53Protocol.PlacementSchema, partition);
        await Assert.That(expected).IsNotNull();
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await root.ReadAtomicPartitionPlacementAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
            await administrator.CallAsync(McpCallerTools.AdminPartitionPlacementRead, request, token))).Value);
    }
}
