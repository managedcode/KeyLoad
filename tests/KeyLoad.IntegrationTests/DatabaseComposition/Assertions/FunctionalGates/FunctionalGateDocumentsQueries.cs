using System.Text.Json;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class FunctionalGateDocumentsQueries
{
    private const string DocumentIdParameter = "docId";

    internal static async Task RequireAsync(FunctionalGateContext context, EntityRef reference, long revision,
        string? json, long cut, CancellationToken token)
    {
        var request = new SqlOperationRequest(context.Partition,
            "SELECT * FROM " + reference.Collection + " WHERE docId = @docId",
            new(StringComparer.Ordinal)
            {
                [DocumentIdParameter] = JsonSerializer.SerializeToElement(reference.Id, JsonDefaults.Options)
            });
        var raw = await context.SdkAsync(context.Reader.Sdk.ExecuteSqlAsync(request, token));
        var sdk = raw.Deserialize<QueryPage>(JsonDefaults.Options) ?? throw new InvalidOperationException("The SQL page is absent.");
        var official = await context.McpAsync<QueryPage>(context.Reader, SqlOperationProtocol.ToolName, request, token);
        await PageAsync(sdk, reference, revision, json, cut);
        await PageAsync(official, reference, revision, json, cut);
    }

    private static async Task PageAsync(QueryPage actual, EntityRef reference, long revision, string? json, long cut)
    {
        await Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(cut);
        QueryRow[] rows = json is null ? [] : [new(reference.Id, revision, json, false, [])];
        await FunctionalGateDocumentsReads.EqualAsync(new QueryPage([.. rows], null,
            actual.CutPosition, "index:by_doc_id"), actual);
    }
}
