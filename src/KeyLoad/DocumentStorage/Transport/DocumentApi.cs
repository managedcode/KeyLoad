namespace KeyLoad.Server;

internal static class DocumentApi
{
    private const string CommandPath = "/commands";
    private const string GetPath = "/documents/get";

    internal static bool IsCommandPath(PathString path) => path == CommandPath;

    internal static void Map(WebApplication app)
    {
        app.MapPost(CommandPath, (CommandRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.Batch, request.CommandId, request));
        app.MapPost(GetPath, (GetDocumentRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Clustering.GrainReadKind.Document, request));
    }
}
