using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using C = KeyLoad.IntegrationTests.Features.DocumentStorage.RemoteDocumentRf3Protocol;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

internal static class DocumentLifetimeRf3Assertions
{
    private const string RawGrant = "*";

    internal static async Task PrincipalAsync(KeyLoadClient admin, RemoteDocumentRf3Seed seed,
        long epoch, bool raw, CancellationToken token)
    {
        var expected = seed.Principal with
        {
            PolicyEpoch = epoch,
            FieldGrants = raw ? [RawGrant] : [C.Title]
        };
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await admin.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
    }

    internal static async Task HealthyAsync(RequestCqrsRf3Callers reader, KeyLoadClient source,
        KeyLoadClient target, RemoteDocumentRf3Seed seed, CancellationToken token)
    {
        var sourceBefore = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var targetBefore = await McpCallerAssertions.SdkSuccessAsync(await target.StatusAsync(token));
        var request = new GetDocumentRequest(C.Reference, seed.Receipt.Token);
        await RemoteDocumentRf3Assertions.DocumentAsync(await PublicReadAssertions.DocumentAsync(
            await reader.Sdk.GetAsync(C.Reference, seed.Receipt.Token, token)), projected: true);
        await RemoteDocumentRf3Assertions.DocumentAsync((await PublicReadAssertions.DocumentAsync(
            await reader.Mcp.CallAsync(C.DocumentsGet, request, token))).Value, projected: true);
        var sourceAfter = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var targetAfter = await McpCallerAssertions.SdkSuccessAsync(await target.StatusAsync(token));
        await Assert.That(sourceAfter.Applied).IsEqualTo(sourceBefore.Applied);
        await Assert.That(targetAfter.Applied).IsEqualTo(targetBefore.Applied);
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers target, RemoteDocumentRf3Seed seed, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(seed.Receipt,
            await McpCallerAssertions.SdkSuccessAsync(await target.Sdk.CommitAsync(seed.Command, token)));
        await SqlRf3Protocol.EqualAsync(seed.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await target.Mcp.CallAsync(C.DocumentsCommit, seed.Command, token))).Value);
    }
}
