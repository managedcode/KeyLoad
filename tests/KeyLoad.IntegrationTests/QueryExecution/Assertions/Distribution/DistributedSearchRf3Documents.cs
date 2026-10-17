using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class DistributedSearchRf3Documents
{
    internal static async Task RequireAsync(KeyLoadClient source, KeyLoadClient destination, CancellationToken token)
    {
        await OneAsync(destination, RemoteDocumentRf3Protocol.Reference, RemoteDocumentRf3Protocol.OriginalJson, token);
        await OneAsync(source, new(RemotePartitionQueryRf3Seed.Local, RemoteDocumentRf3Protocol.Collection,
            RemoteDocumentRf3Protocol.Document), RemotePartitionQueryRf3Seed.SourceJson, token);
        await OneAsync(source, new(RemotePartitionQueryRf3Seed.Local, RemoteDocumentRf3Protocol.Collection,
            DistributedSearchRf3Seed.LongId), DistributedSearchRf3Seed.LongJson, token);
        await OneAsync(source, new(RemotePartitionQueryRf3Seed.Local, RemoteDocumentRf3Protocol.Collection,
            DistributedSearchRf3Seed.MissingId), DistributedSearchRf3Seed.MissingJson, token);
    }

    private static async Task OneAsync(KeyLoadClient administrator, EntityRef reference,
        string json, CancellationToken token)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await administrator.GetAsync(reference, token));
        var literal = new DocumentResult(reference, DistributedSearchRf3Seed.Revision, json, false, []);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(literal))).IsTrue();
    }
}
