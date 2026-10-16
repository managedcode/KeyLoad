using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlOffsetRf3Sources
{
    internal static async Task UnchangedAsync(RequestCqrsRf3Callers callers, SqlOffsetRf3State state, CancellationToken token)
    {
        foreach (var (collection, id, json) in new[]
        {
            (SqlOffsetRf3Protocol.Left, "a", """{"order_id":"a","customer_id":"c1","title":"first"}"""),
            (SqlOffsetRf3Protocol.Left, "b", """{"order_id":"b","customer_id":"c2","title":"second"}"""),
            (SqlOffsetRf3Protocol.Left, "c", """{"order_id":"c","customer_id":"c3","title":"third"}"""),
            (SqlOffsetRf3Protocol.Left, "d", """{"order_id":"d","customer_id":"c4","title":"fourth"}"""),
            (SqlOffsetRf3Protocol.Right, "c1", """{"id":"c1","name":"Aye"}"""),
            (SqlOffsetRf3Protocol.Right, "c2", """{"id":"c2","name":"Bee"}"""),
            (SqlOffsetRf3Protocol.Right, "c3", """{"id":"c3","name":"See"}"""),
            (SqlOffsetRf3Protocol.Right, "c4", """{"id":"c4","name":"Dee"}""")
        })
        {
            var reference = new EntityRef(state.Partition, collection, id);
            var actual = await PublicReadAssertions.DocumentAsync(await callers.Sdk.GetAsync(reference, state.Receipt.Token, token))
                ?? throw new InvalidOperationException("The literal OFFSET source is absent.");
            var expected = new DocumentResult(reference, SqlOffsetRf3Protocol.InitialRevision, json, false, []);
            await SqlRf3Protocol.EqualAsync(expected, actual);
            var officialDocument = (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(await callers.Mcp.CallAsync(
                McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, state.Receipt.Token), token))).Value;
            await SqlRf3Protocol.EqualAsync(new DocumentReadResult(expected), officialDocument);
        }
        var replay = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(state.Seed, token));
        await SqlRf3Protocol.EqualAsync(state.Receipt, replay);
        await Assert.That(NativeSerialization.Serialize(replay).AsSpan().SequenceEqual(NativeSerialization.Serialize(state.Receipt))).IsTrue();
        await Assert.That(replay.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        var official = await SqlRf3Protocol.McpAsync<CommitReceipt>(callers.Mcp, SqlRf3Protocol.Call(state.Partition, McpCallerTools.DocumentsCommit, state.Seed) with
        {
            CommandId = state.Seed.CommandId
        }, token);
        await SqlRf3Protocol.EqualAsync(state.Receipt, official);
        await Assert.That(NativeSerialization.Serialize(official).AsSpan().SequenceEqual(NativeSerialization.Serialize(state.Receipt))).IsTrue();
    }
}
