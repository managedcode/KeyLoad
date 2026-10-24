namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class SqlBetweenSymmetricClusterImage
{
    internal static async Task<string> ReadAsync(global::KeyLoad.IntegrationTests.OrleansExecution.RequestCqrsClusterCallers root, SqlBetweenSymmetricClusterState state, CancellationToken token)
    {
        var documents = new List<DocumentReadResult>();
        foreach (var row in SqlBetweenSymmetricClusterState.Rows)
        {
            var reference = new EntityRef(PostgresClusterState.Partition, SqlNullOrderingClusterProtocol.Collection, row.Id);
            var expected = new DocumentReadResult(new(reference,
                row.Id == SqlBetweenSymmetricClusterState.Low ? state.LowRevision : SqlNullOrderingClusterProtocol.SeedRevision,
                row.Id == SqlBetweenSymmetricClusterState.Low ? state.LowJson : row.Json, false, []));
            var actual = await global::KeyLoad.IntegrationTests.ClientApi.McpCallerAssertions.SdkSuccessAsync(await root.Sdk.GetAsync(reference, state.Current.Token, token));
            await SqlClusterProtocol.EqualAsync(expected, actual);
            await SqlClusterProtocol.EqualAsync(expected, (await global::KeyLoad.IntegrationTests.ClientApi.McpCallerAssertions.SuccessAsync<DocumentReadResult>(
                await root.Mcp.CallAsync(global::KeyLoad.IntegrationTests.ClientApi.McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, state.Current.Token), token))).Value);
            documents.Add(actual);
        }
        var outbox = await global::KeyLoad.IntegrationTests.ClientApi.McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(PostgresClusterState.Partition, token));
        return global::System.Text.Json.JsonSerializer.Serialize(new
        {
            Documents = documents,
            Outbox = outbox
        }, JsonDefaults.Options);
    }

    internal static async Task PageAsync(QueryPage actual, SqlBetweenSymmetricClusterCase item, long lowRevision)
    {
        var expected = new QueryPage([.. item.Ids.Select(id => new QueryRow(id,
            id == SqlBetweenSymmetricClusterState.Low ? lowRevision : SqlNullOrderingClusterProtocol.SeedRevision,
            "{\"label\":\"" + id + "\"}", RedactedFields: []))], null, actual.CutPosition, SqlBetweenSymmetricClusterState.AccessPath);
        await global::TUnit.Assertions.Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(default(long));
        await SqlClusterProtocol.EqualAsync(expected, actual);
    }
}
