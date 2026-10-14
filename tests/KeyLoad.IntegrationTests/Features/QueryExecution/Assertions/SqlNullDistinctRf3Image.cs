namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlNullDistinctRf3Image
{
    internal static async Task<string> ReadAsync(global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers root, SqlNullDistinctRf3State state, CancellationToken token)
    {
        var documents = new List<DocumentReadResult>();
        foreach (var row in SqlNullOrderingRf3State.Rows)
        {
            var reference = new EntityRef(PostgresRf3State.Partition, SqlNullOrderingRf3Protocol.Collection, row.Id);
            var expected = new DocumentReadResult(new(reference,
                row.Id == SqlNullDistinctRf3State.Low ? state.LowRevision : SqlNullOrderingRf3Protocol.SeedRevision,
                row.Id == SqlNullDistinctRf3State.Low ? state.LowJson : row.Json, false, []));
            var actual = await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(await root.Sdk.GetAsync(reference, state.Current.Token, token));
            await SqlRf3Protocol.EqualAsync(expected, actual);
            await SqlRf3Protocol.EqualAsync(expected, (await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SuccessAsync<DocumentReadResult>(
                await root.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, state.Current.Token), token))).Value);
            documents.Add(actual);
        }
        var outbox = await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(PostgresRf3State.Partition, token));
        return global::System.Text.Json.JsonSerializer.Serialize(new
        {
            Documents = documents,
            Outbox = outbox
        }, JsonDefaults.Options);
    }

    internal static async Task PageAsync(QueryPage actual, SqlNullDistinctRf3Case item, long lowRevision)
    {
        var expected = new QueryPage([.. item.Ids.Select(id => new QueryRow(id,
            id == SqlNullDistinctRf3State.Low ? lowRevision : SqlNullOrderingRf3Protocol.SeedRevision,
            "{\"label\":\"" + id + "\"}", RedactedFields: []))], null, actual.CutPosition, SqlNullDistinctRf3State.AccessPath);
        await global::TUnit.Assertions.Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(default(long));
        await SqlRf3Protocol.EqualAsync(expected, actual);
    }
}
