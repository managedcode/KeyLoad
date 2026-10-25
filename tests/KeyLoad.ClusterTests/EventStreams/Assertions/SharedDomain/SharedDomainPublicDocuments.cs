using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ProcessRecoveryTests.EventStreams;
using static KeyLoad.ProcessRecoveryTests.EventStreams.SharedDomainProtocol;

namespace KeyLoad.ClusterTests.EventStreams;

internal static class SharedDomainPublicDocuments
{
    internal static async Task RequireAsync(KeyLoadClient sdk, McpOfficialClient official,
        SharedDomainPublicState state, SharedDomainScope scope, CancellationToken token)
    {
        if (scope.Documents is not { } collection)
        {
            return;
        }
        var lifetime = state.Lifetimes[collection];
        await OneAsync(sdk, official, scope, collection, lifetime, Item, true, token);
        await OneAsync(sdk, official, scope, collection, lifetime, Processed,
            state.Processed && scope.Partition.TransactionDomainId == FirstDomain, token);
        await OneAsync(sdk, official, scope, collection, lifetime, Healthy, state.HealthyWritten, token);
        await OneAsync(sdk, official, scope, collection, lifetime, Refused, false, token);
    }

    private static async Task OneAsync(KeyLoadClient sdk, McpOfficialClient official, SharedDomainScope scope,
        string collection, EntityRefLifetime lifetime, string id, bool present, CancellationToken token)
    {
        var reference = new EntityRef(scope.Partition, collection, id);
        var expected = present ? new DocumentResult(reference with { Lifetime = lifetime }, FirstRevision, scope.Payload, false, []) : null;
        await SharedDomainPublicValues.EqualAsync(await PublicReadAssertions.DocumentAsync(await sdk.GetAsync(reference, token)), expected);
        var actual = (await PublicReadAssertions.DocumentAsync(await official.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(reference), token))).Value;
        await SharedDomainPublicValues.EqualAsync(actual, expected);
    }
}
