using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal static class FunctionalGateColdRetainedRows
{
    private const long FirstRevision = 1;
    private const long AbsentRevision = 0;
    private const string ReaderProbePrefix = "reader-after-second-cold-";
    private const string ReaderProbeJson = "{\"stage\":\"reader-write-after-second-cold\"}";
    private const string AnchorJson = "{\"purpose\":\"cold-anchor\"}";

    internal static async Task ReadAsync(FunctionalGateContext context, DocumentReadResult expected,
        CancellationToken token)
    {
        var reference = expected.Document!.Reference;
        await Assert.That(reference.Lifetime).IsNotNull();
        await SqlClusterProtocol.EqualAsync(expected, await context.SdkAsync(context.Reader.Sdk.GetAsync(reference, token)));
        await SqlClusterProtocol.EqualAsync(expected, await context.McpAsync<DocumentReadResult>(context.Reader,
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token));
    }

    internal static async Task ReaderAsync(FunctionalGateContext original, FunctionalGateColdState state,
        Func<FunctionalGateContext, Task> unchanged, CancellationToken token)
    {
        var context = original with
        {
            Writer = original.Reader,
            WriterPrincipalId = state.Reader.Principal.Id
        };
        var reference = original.Bind(new(state.Partition, original.MutableDocumentCollection,
            ReaderProbePrefix + Guid.NewGuid().ToString(FunctionalGateProtocol.GuidFormat)));
        var command = new CommandRequest(Guid.NewGuid(), state.Partition,
            [new PutDocument(reference.Collection, reference.Id, ReaderProbeJson, AbsentRevision)]);
        await FunctionalGateColdRetainedRefusals.AllAsync(context, command, unchanged, token);
        await SqlClusterProtocol.EqualAsync(new DocumentReadResult(null), await context.SdkAsync(
            context.Reader.Sdk.GetAsync(reference, token)));
        await SqlClusterProtocol.EqualAsync(new DocumentReadResult(null), await context.McpAsync<DocumentReadResult>(
            context.Reader, McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token));
    }

    internal static async Task OperatorAsync(FunctionalGateContext original, FunctionalGateColdState state,
        Func<FunctionalGateContext, Task> unchanged, CancellationToken token)
    {
        var context = original with
        {
            Reader = original.QueueAdministrator,
            Writer = original.QueueAdministrator,
            ReaderPrincipalId = state.QueueOperator.Principal.Id,
            WriterPrincipalId = state.QueueOperator.Principal.Id
        };
        await ReadAsync(context, new(new(original.Bind(state.Anchor), FirstRevision, AnchorJson, false, [])), token);
        await unchanged(context);
    }
}
