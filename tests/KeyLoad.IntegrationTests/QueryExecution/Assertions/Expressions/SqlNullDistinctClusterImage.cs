namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlNullDistinctClusterImage
{
    internal static async Task<string> ReadAsync(global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsClusterCallers root, SqlNullDistinctClusterState state, CancellationToken token)
    {
        var documents = new List<DocumentReadResult>();
        foreach (var row in SqlNullOrderingClusterState.Rows)
        {
            var reference = new EntityRef(PostgresClusterState.Partition, SqlNullOrderingClusterProtocol.Collection, row.Id);
            var expected = new DocumentReadResult(new(reference,
                row.Id == SqlNullDistinctClusterState.Low ? state.LowRevision : SqlNullOrderingClusterProtocol.SeedRevision,
                row.Id == SqlNullDistinctClusterState.Low ? state.LowJson : row.Json, false, []));
            var actual = await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(await root.Sdk.GetAsync(reference, state.Current.Token, token));
            await SqlClusterProtocol.EqualAsync(expected, actual);
            await SqlClusterProtocol.EqualAsync(expected, (await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SuccessAsync<DocumentReadResult>(
                await root.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, state.Current.Token), token))).Value);
            documents.Add(actual);
        }
        var outbox = await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(PostgresClusterState.Partition, token));
        return global::System.Text.Json.JsonSerializer.Serialize(new
        {
            Documents = documents,
            Outbox = outbox
        }, JsonDefaults.Options);
    }

    internal static async Task PageAsync(QueryPage actual, SqlNullDistinctClusterCase item, long lowRevision)
    {
        var expected = new QueryPage([.. item.Ids.Select(id => new QueryRow(id,
            id == SqlNullDistinctClusterState.Low ? lowRevision : SqlNullOrderingClusterProtocol.SeedRevision,
            "{\"label\":\"" + id + "\"}", RedactedFields: []))], null, actual.CutPosition, SqlNullDistinctClusterState.AccessPath);
        await global::TUnit.Assertions.Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(default(long));
        await SqlClusterProtocol.EqualAsync(expected, actual);
    }
}
