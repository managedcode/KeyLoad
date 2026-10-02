namespace KeyLoad.Server;

internal static class DocumentApi
{
    private const string CommandPath = "/v1/commands";
    private const string GetPath = "/v1/documents/get";

    internal static void Map(WebApplication app)
    {
        app.MapPost(CommandPath, (CommandRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.Batch, request.CommandId, request));
        app.MapPost(GetPath, (GetDocumentRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Orleans.GrainReadKind.Document, request));
    }
}
