using KeyLoad.Client;
using KeyLoad.CrashHost.Features.EventStreams;
using KeyLoad.IntegrationTests.Features.ClientApi;
using static KeyLoad.CrashHost.Features.EventStreams.SharedDomainProtocol;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class SharedDomainPublicDocuments
{
    internal static async Task RequireAsync(KeyLoadClient sdk, McpOfficialClient official,
        SharedDomainPublicState state, SharedDomainScope scope, CancellationToken token)
    {
        if (scope.Documents is not { } collection)
        {
            return;
        }
        await OneAsync(sdk, official, scope, collection, Item, true, token);
        await OneAsync(sdk, official, scope, collection, Processed,
            state.Processed && scope.Partition.TransactionDomainId == FirstDomain, token);
        await OneAsync(sdk, official, scope, collection, Healthy, state.HealthyWritten, token);
        await OneAsync(sdk, official, scope, collection, Refused, false, token);
    }

    private static async Task OneAsync(KeyLoadClient sdk, McpOfficialClient official, SharedDomainScope scope,
        string collection, string id, bool present, CancellationToken token)
    {
        var reference = new EntityRef(scope.Partition, collection, id);
        var expected = present ? new DocumentResult(reference, FirstRevision, scope.Payload, false, []) : null;
        await SharedDomainPublicValues.EqualAsync(await PublicReadAssertions.DocumentAsync(await sdk.GetAsync(reference, token)), expected);
        var actual = (await PublicReadAssertions.DocumentAsync(await official.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(reference), token))).Value;
        await SharedDomainPublicValues.EqualAsync(actual, expected);
    }
}
