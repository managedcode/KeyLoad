using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class AdversarialAuthorityAssertions
{
    internal static Task DocumentAsync(AdversarialAuthoritySeed seed, DocumentResult actual)
        => SqlRf3Protocol.EqualAsync(new DocumentResult(seed.Scenario.Reference,
            McpCallerProtocol.FirstRevision, McpDocumentProtocol.InitialJson, false, []), actual);

    internal static async Task<AdversarialAuthorityImage> ImageAsync(KeyLoadClient root,
        AdversarialAuthorityState state, CancellationToken token)
    {
        var owned = await McpCallerAssertions.SdkSuccessAsync(await root.GetAsync(state.Owned.Scenario.Reference, token));
        var foreign = await McpCallerAssertions.SdkSuccessAsync(await root.GetAsync(state.Foreign.Scenario.Reference, token));
        await Assert.That(owned).IsNotNull();
        await Assert.That(foreign).IsNotNull();
        await DocumentAsync(state.Owned, owned!);
        await DocumentAsync(state.Foreign, foreign!);
        var ownedOutbox = await McpCallerAssertions.SdkSuccessAsync(await root.OutboxStatusAsync(state.Owned.Scenario.Partition, token));
        var foreignOutbox = await McpCallerAssertions.SdkSuccessAsync(await root.OutboxStatusAsync(state.Foreign.Scenario.Partition, token));
        return new(owned!, foreign!, ownedOutbox, foreignOutbox);
    }

    internal static async Task HealthyAsync(KeyLoadClient reader, McpOfficialClient session,
        AdversarialAuthorityState state, CancellationToken token)
    {
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await reader.GetAsync(state.Owned.Scenario.Reference, token));
        await Assert.That(sdk).IsNotNull();
        await DocumentAsync(state.Owned, sdk!);
        var official = await McpCallerAssertions.SuccessAsync<DocumentResult>(await session.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(state.Owned.Scenario.Reference), token));
        await DocumentAsync(state.Owned, official.Value);
        await Assert.That(official.Value.Reference).IsEqualTo(state.Owned.Scenario.Reference);
        await Assert.That(official.Value.Revision).IsEqualTo(McpCallerProtocol.FirstRevision);
    }

    internal static async Task ReplayAsync(KeyLoadClient root, McpOfficialClient rootSession,
        AdversarialAuthorityState state, CancellationToken token)
    {
        var before = await ImageAsync(root, state, token);
        foreach (var seed in new[] { state.Owned, state.Foreign })
        {
            var sdk = await McpCallerAssertions.SdkSuccessAsync(await root.CommitAsync(seed.Command, token));
            await SqlRf3Protocol.EqualAsync(seed.Receipt, sdk);
            var official = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await rootSession.CallAsync(
                McpCallerTools.DocumentsCommit, seed.Command, token));
            await SqlRf3Protocol.EqualAsync(seed.Receipt, official.Value);
        }
        await SqlRf3Protocol.EqualAsync(before, await ImageAsync(root, state, token));
    }
}
