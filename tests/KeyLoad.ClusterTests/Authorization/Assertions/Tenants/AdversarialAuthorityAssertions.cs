using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.DocumentStorage;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.Authorization;

internal static class AdversarialAuthorityAssertions
{
    internal static Task DocumentAsync(AdversarialAuthoritySeed seed, DocumentResult actual)
        => SqlClusterProtocol.EqualAsync(new DocumentResult(seed.Scenario.StoredReference,
            McpCallerProtocol.FirstRevision, McpDocumentProtocol.InitialJson, false, []), actual);

    internal static async Task<AdversarialAuthorityImage> ImageAsync(KeyLoadClient root,
        AdversarialAuthorityState state, CancellationToken token)
    {
        var owned = await PublicReadAssertions.DocumentAsync(await root.GetAsync(state.Owned.Scenario.Reference, token));
        var foreign = await PublicReadAssertions.DocumentAsync(await root.GetAsync(state.Foreign.Scenario.Reference, token));
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
        var sdk = await PublicReadAssertions.DocumentAsync(await reader.GetAsync(state.Owned.Scenario.Reference, token));
        await Assert.That(sdk).IsNotNull();
        await DocumentAsync(state.Owned, sdk!);
        var official = await PublicReadAssertions.RequiredDocumentAsync(await session.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(state.Owned.Scenario.Reference), token));
        await DocumentAsync(state.Owned, official.Value);
        await Assert.That(official.Value.Reference).IsEqualTo(state.Owned.Scenario.StoredReference);
        await Assert.That(official.Value.Revision).IsEqualTo(McpCallerProtocol.FirstRevision);
    }

    internal static async Task ReplayAsync(KeyLoadClient root, McpOfficialClient rootSession,
        AdversarialAuthorityState state, CancellationToken token)
    {
        var before = await ImageAsync(root, state, token);
        foreach (var seed in new[] { state.Owned, state.Foreign })
        {
            var sdk = await McpCallerAssertions.SdkSuccessAsync(await root.CommitAsync(seed.Command, token));
            await SqlClusterProtocol.EqualAsync(seed.Receipt, sdk);
            var official = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await rootSession.CallAsync(
                McpCallerTools.DocumentsCommit, seed.Command, token));
            await SqlClusterProtocol.EqualAsync(seed.Receipt, official.Value);
        }
        await SqlClusterProtocol.EqualAsync(before, await ImageAsync(root, state, token));
    }
}
