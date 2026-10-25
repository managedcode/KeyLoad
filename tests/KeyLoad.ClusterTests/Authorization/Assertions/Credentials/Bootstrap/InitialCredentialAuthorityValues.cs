using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.DocumentStorage;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.Authorization;

internal static class InitialCredentialAuthorityValues
{
    private static DocumentResult Expected(InitialCredentialAuthorityState state)
        => new(state.Seed.Scenario.StoredReference, McpCallerProtocol.FirstRevision, McpDocumentProtocol.InitialJson, false, []);

    internal static async Task HealthyAsync(global::KeyLoad.Client.KeyLoadClient root, global::KeyLoad.Client.KeyLoadClient reader, McpOfficialClient session,
        InitialCredentialAuthorityState state, CancellationToken token)
    {
        var before = await ImageAsync(root, state, token);
        var sdk = await PublicReadAssertions.DocumentAsync(await reader.GetAsync(state.Seed.Scenario.Reference, token));
        await Assert.That(sdk).IsNotNull();
        await SqlClusterProtocol.EqualAsync<DocumentResult?>(Expected(state), sdk);
        var official = await PublicReadAssertions.RequiredDocumentAsync(await session.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(state.Seed.Scenario.Reference), token));
        await SqlClusterProtocol.EqualAsync(Expected(state), official.Value);
        await SqlClusterProtocol.EqualAsync(before, await ImageAsync(root, state, token));
    }

    internal static async Task<DiscoveryAuthorityImage> ImageAsync(global::KeyLoad.Client.KeyLoadClient root,
        InitialCredentialAuthorityState state, CancellationToken token)
    {
        var document = await PublicReadAssertions.DocumentAsync(await root.GetAsync(state.Seed.Scenario.Reference, token));
        await Assert.That(document).IsNotNull();
        await SqlClusterProtocol.EqualAsync<DocumentResult?>(Expected(state), document);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await root.OutboxStatusAsync(state.Seed.Scenario.Partition, token));
        return new(document, outbox);
    }

    internal static async Task ReplayAsync(global::KeyLoad.Client.KeyLoadClient root, InitialCredentialAuthorityState state, CancellationToken token)
    {
        var before = await ImageAsync(root, state, token);
        var replay = await McpCallerAssertions.SdkSuccessAsync(await root.CommitAsync(state.Seed.Command, token));
        await SqlClusterProtocol.EqualAsync(state.Seed.Receipt, replay);
        await SqlClusterProtocol.EqualAsync(before, await ImageAsync(root, state, token));
    }
}
