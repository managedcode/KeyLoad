using System.Text;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class FunctionalGateDocumentsReads
{
    private const int MaximumDiagnosticBytes = 4096;

    internal static async Task PairAsync(FunctionalGateContext context, FunctionalGateDocumentsState state,
        long independentRevision, long relatedRevision, string status, bool independentLinked, bool relatedLinked,
        long cut, CancellationToken token)
    {
        await DocumentAsync(context, state.Independent, independentRevision,
            state.IndependentJson(context.Bind(context.Anchor), status, independentLinked), token);
        await DocumentAsync(context, state.Related, relatedRevision, state.RelatedJson(context.Bind(context.Anchor), relatedLinked), token);
        await FunctionalGateDocumentsQueries.RequireAsync(context, state.Independent, independentRevision,
            state.IndependentJson(context.Bind(context.Anchor), status, independentLinked), cut, token);
        await FunctionalGateDocumentsQueries.RequireAsync(context, state.Related, relatedRevision,
            state.RelatedJson(context.Bind(context.Anchor), relatedLinked), cut, token);
        await AnchorAsync(context, token);
    }

    internal static Task AnchorAsync(FunctionalGateContext context, CancellationToken token)
        => DocumentAsync(context, context.Bind(context.Anchor), 1, FunctionalGateDocumentsState.AnchorJson, token);

    internal static async Task DocumentAsync(FunctionalGateContext context, EntityRef reference, long revision,
        string json, CancellationToken token)
    {
        reference = context.Bind(reference);
        await EqualAsync(context.Partition, reference.Partition);
        var expected = new DocumentReadResult(new(reference, revision, json, false, []));
        await EqualAsync(expected, await context.SdkAsync(context.Reader.Sdk.GetAsync(reference, token)));
        await EqualAsync(expected, await context.McpAsync<DocumentReadResult>(context.Reader,
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token));
    }

    internal static async Task AbsentAsync(FunctionalGateContext context, EntityRef reference, long cut, CancellationToken token)
    {
        var expected = new DocumentReadResult(null);
        await EqualAsync(expected, await context.SdkAsync(context.Reader.Sdk.GetAsync(reference, token)));
        await EqualAsync(expected, await context.McpAsync<DocumentReadResult>(context.Reader,
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token));
        await FunctionalGateDocumentsQueries.RequireAsync(context, reference, 0, null, cut, token);
    }

    internal static async Task EqualAsync<T>(T expected, T actual)
    {
        var actualBytes = JsonDefaults.Serialize(actual);
        var expectedBytes = JsonDefaults.Serialize(expected);
        await Assert.That(actualBytes.AsSpan().SequenceEqual(expectedBytes)).IsTrue()
            .Because($"Expected UTF8 JSON ({expectedBytes.Length} bytes): {Diagnostic(expectedBytes)}; "
                + $"actual UTF8 JSON ({actualBytes.Length} bytes): {Diagnostic(actualBytes)}.");
    }

    private static string Diagnostic(byte[] bytes)
    {
        var length = Math.Min(bytes.Length, MaximumDiagnosticBytes);
        return Encoding.UTF8.GetString(bytes.AsSpan(0, length));
    }
}
