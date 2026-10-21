using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class DiscoveryAuthorityValues
{
    internal static DocumentResult Expected(DiscoveryAuthorityState state)
        => new(state.Scenario.StoredReference, McpCallerProtocol.FirstRevision, McpDocumentProtocol.InitialJson, false, []);

    internal static async Task<Guid> FirstAsync(McpOfficialClient session, DiscoveryAuthorityState state, CancellationToken token)
    {
        await session.Client.ListToolsAsync(cancellationToken: token);
        var first = await PublicReadAssertions.RequiredDocumentAsync(await session.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(state.Scenario.Reference), token));
        await SqlClusterProtocol.EqualAsync(Expected(state), first.Value);
        return first.RequestId;
    }

    internal static async Task HealthyAsync(KeyLoadClient root, KeyLoadClient reader, McpOfficialClient session,
        DiscoveryAuthorityState state, CancellationToken token)
    {
        var before = await ImageAsync(root, state, token);
        var sdk = await PublicReadAssertions.DocumentAsync(await reader.GetAsync(state.Scenario.Reference, token));
        await Assert.That(sdk).IsNotNull();
        await SqlClusterProtocol.EqualAsync<DocumentResult?>(Expected(state), sdk);
        var official = await PublicReadAssertions.RequiredDocumentAsync(await session.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(state.Scenario.Reference), token));
        await SqlClusterProtocol.EqualAsync(Expected(state), official.Value);
        await SqlClusterProtocol.EqualAsync(before, await ImageAsync(root, state, token));
    }

    internal static async Task<DiscoveryAuthorityImage> ImageAsync(KeyLoadClient root, DiscoveryAuthorityState state, CancellationToken token)
    {
        var document = await PublicReadAssertions.DocumentAsync(await root.GetAsync(state.Scenario.Reference, token));
        await Assert.That(document).IsNotNull();
        await SqlClusterProtocol.EqualAsync<DocumentResult?>(Expected(state), document);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await root.OutboxStatusAsync(state.Scenario.Partition, token));
        return new(document, outbox);
    }

    internal static async Task ReplayAsync(KeyLoadClient root, DiscoveryAuthorityState state, CancellationToken token)
    {
        var original = state.OriginalRepairResult ?? throw new InvalidOperationException(DiscoveryAuthorityProtocol.MissingRepairResult);
        await SqlClusterProtocol.EqualAsync(state.Repaired, original);
        var before = await ImageAsync(root, state, token);
        var replay = await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(
            state.RepairCommandId, state.Repaired, token));
        await SqlClusterProtocol.EqualAsync(original, replay);
        await SqlClusterProtocol.EqualAsync(before, await ImageAsync(root, state, token));
    }
}
