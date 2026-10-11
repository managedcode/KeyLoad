using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class InitialCredentialAuthorityValues
{
    private static DocumentResult Expected(InitialCredentialAuthorityState state)
        => new(state.Seed.Scenario.Reference, McpCallerProtocol.FirstRevision, McpDocumentProtocol.InitialJson, false, []);

    internal static async Task HealthyAsync(global::KeyLoad.Client.KeyLoadClient root, global::KeyLoad.Client.KeyLoadClient reader, McpOfficialClient session,
        InitialCredentialAuthorityState state, CancellationToken token)
    {
        var before = await ImageAsync(root, state, token);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await reader.GetAsync(state.Seed.Scenario.Reference, token));
        await Assert.That(sdk).IsNotNull();
        await SqlRf3Protocol.EqualAsync<DocumentResult?>(Expected(state), sdk);
        var official = await McpCallerAssertions.SuccessAsync<DocumentResult>(await session.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(state.Seed.Scenario.Reference), token));
        await SqlRf3Protocol.EqualAsync(Expected(state), official.Value);
        await SqlRf3Protocol.EqualAsync(before, await ImageAsync(root, state, token));
    }

    internal static async Task<DiscoveryAuthorityImage> ImageAsync(global::KeyLoad.Client.KeyLoadClient root,
        InitialCredentialAuthorityState state, CancellationToken token)
    {
        var document = await McpCallerAssertions.SdkSuccessAsync(await root.GetAsync(state.Seed.Scenario.Reference, token));
        await Assert.That(document).IsNotNull();
        await SqlRf3Protocol.EqualAsync<DocumentResult?>(Expected(state), document);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await root.OutboxStatusAsync(state.Seed.Scenario.Partition, token));
        return new(document, outbox);
    }

    internal static async Task ReplayAsync(global::KeyLoad.Client.KeyLoadClient root, InitialCredentialAuthorityState state, CancellationToken token)
    {
        var before = await ImageAsync(root, state, token);
        var replay = await McpCallerAssertions.SdkSuccessAsync(await root.CommitAsync(state.Seed.Command, token));
        await SqlRf3Protocol.EqualAsync(state.Seed.Receipt, replay);
        await SqlRf3Protocol.EqualAsync(before, await ImageAsync(root, state, token));
    }
}
