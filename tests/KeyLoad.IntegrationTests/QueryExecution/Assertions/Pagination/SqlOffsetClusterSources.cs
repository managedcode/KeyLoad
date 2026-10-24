using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class SqlOffsetClusterSources
{
    internal static async Task UnchangedAsync(RequestCqrsClusterCallers callers, SqlOffsetClusterState state, CancellationToken token)
    {
        foreach (var (collection, id, json) in new[]
        {
            (SqlOffsetClusterProtocol.Left, "a", """{"order_id":"a","customer_id":"c1","title":"first"}"""),
            (SqlOffsetClusterProtocol.Left, "b", """{"order_id":"b","customer_id":"c2","title":"second"}"""),
            (SqlOffsetClusterProtocol.Left, "c", """{"order_id":"c","customer_id":"c3","title":"third"}"""),
            (SqlOffsetClusterProtocol.Left, "d", """{"order_id":"d","customer_id":"c4","title":"fourth"}"""),
            (SqlOffsetClusterProtocol.Right, "c1", """{"id":"c1","name":"Aye"}"""),
            (SqlOffsetClusterProtocol.Right, "c2", """{"id":"c2","name":"Bee"}"""),
            (SqlOffsetClusterProtocol.Right, "c3", """{"id":"c3","name":"See"}"""),
            (SqlOffsetClusterProtocol.Right, "c4", """{"id":"c4","name":"Dee"}""")
        })
        {
            var reference = new EntityRef(state.Partition, collection, id);
            var actual = await PublicReadAssertions.DocumentAsync(await callers.Sdk.GetAsync(reference, state.Receipt.Token, token))
                ?? throw new InvalidOperationException("The literal OFFSET source is absent.");
            var expected = new DocumentResult(reference, SqlOffsetClusterProtocol.InitialRevision, json, false, []);
            await SqlClusterProtocol.EqualAsync(expected, actual);
            var officialDocument = (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(await callers.Mcp.CallAsync(
                McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, state.Receipt.Token), token))).Value;
            await SqlClusterProtocol.EqualAsync(new DocumentReadResult(expected), officialDocument);
        }
        var replay = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(state.Seed, token));
        await SqlClusterProtocol.EqualAsync(state.Receipt, replay);
        await Assert.That(OrleansSerialization.Serialize(replay).AsSpan().SequenceEqual(OrleansSerialization.Serialize(state.Receipt))).IsTrue();
        await Assert.That(replay.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(replay.PersistedCopies).IsEqualTo(3);
        var official = await SqlClusterProtocol.McpAsync<CommitReceipt>(callers.Mcp, SqlClusterProtocol.Call(state.Partition, McpCallerTools.DocumentsCommit, state.Seed) with
        {
            CommandId = state.Seed.CommandId
        }, token);
        await SqlClusterProtocol.EqualAsync(state.Receipt, official);
        await Assert.That(OrleansSerialization.Serialize(official).AsSpan().SequenceEqual(OrleansSerialization.Serialize(state.Receipt))).IsTrue();
    }
}
