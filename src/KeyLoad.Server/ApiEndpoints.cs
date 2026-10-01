using System.Text.Json;
using DotNext.Net.Cluster.Consensus.Raft;
using KeyLoad.Core;
using KeyLoad.Query;

namespace KeyLoad.Server;

public static class ApiEndpoints
{
    private static string Principal(HttpContext context) => (string)context.Items["principal"]!;
    private static async Task<T> Submit<T, TRequest>(HttpContext context, OperationKind kind, Guid id, TRequest request)
    {
        var result = await context.RequestServices.GetRequiredService<OrleansNode>().SubmitAsync(kind, id, Principal(context),
            JsonSerializer.Serialize(request, JsonDefaults.Options), context.RequestAborted);
        return result.Get<T>();
    }
    public static WebApplication MapKeyLoadApi(this WebApplication app)
    {
        app.MapPost("/v1/commands", (CommandRequest request, HttpContext context) => Submit<CommitReceipt, CommandRequest>(context, OperationKind.Batch, request.CommandId, request));
        app.MapPost("/v1/documents/get", (GetDocumentRequest request, DatabaseEngine database, HttpContext context) =>
            database.GetDocument(Principal(context), request.Reference));
        app.MapPost("/v1/streams/read", (ReadStreamRequest request, DatabaseEngine database, HttpContext context) =>
            database.ReadStream(Principal(context), request.Stream, request.AfterRevision, request.Limit));
        app.MapPost("/v1/queues/receive", (ReceiveRequest request, HttpContext context) => Submit<ReceiveResult, ReceiveRequest>(context, OperationKind.Receive, request.RequestId, request));
        app.MapPost("/v1/queues/delivery", (DeliveryCommand request, HttpContext context) => Submit<CommitReceipt, DeliveryCommand>(context, OperationKind.Delivery, request.CommandId, request));
        app.MapPost("/v1/queues/process", (ProcessingRequest request, HttpContext context) => Submit<CommitReceipt, ProcessingRequest>(context, OperationKind.Processing, request.CommandId, request));
        app.MapPost("/v1/queues/inspect", (InspectMessageRequest request, DatabaseEngine database, HttpContext context) =>
            database.InspectMessage(Principal(context), request.Lane, request.Id));
        app.MapPost("/v1/graph/traverse", (TraverseRequest request, DatabaseEngine database, HttpContext context) =>
            database.Traverse(Principal(context), request.Partition, request.Graph, request.Start, request.MaxDepth, request.MaxVertices, request.MaxEdges, request.Labels));
        app.MapPost("/v1/series/read", (ReadSamplesRequest request, DatabaseEngine database, HttpContext context) =>
            database.ReadSamples(Principal(context), request.Partition, request.Set, request.SeriesId, request.From, request.Until, request.Limit));
        app.MapPost("/v1/query", (QueryRequest request, QueryEngine queries, HttpContext context) => queries.Execute(Principal(context), request));
        app.MapPost("/v1/search", (SearchRequest request, SearchEngine search, HttpContext context) => search.Search(Principal(context), request));
        app.MapPost("/v1/admin/resources", (ConfigureResourceRequest request, HttpContext context) =>
            Submit<ResourceDefinition, ConfigureResourceRequest>(context, OperationKind.ConfigureResource, CommandId(context), request));
        app.MapPost("/v1/admin/principals", (ConfigurePrincipalRequest request, HttpContext context) =>
            Submit<PrincipalRecord, ConfigurePrincipalRequest>(context, OperationKind.ConfigurePrincipal, CommandId(context), request));
        app.MapPost("/v1/admin/api-keys", (ConfigureApiKeyRequest request, HttpContext context) =>
            Submit<bool, ConfigureApiKeyRequest>(context, OperationKind.ConfigureApiKey, CommandId(context), request));
        app.MapPost("/v1/admin/dispatch", (bool paused, HttpContext context) =>
            Submit<bool, bool>(context, OperationKind.SetDispatch, CommandId(context), paused));
        app.MapPost("/v1/admin/backup", (DatabaseEngine database, NodeOptions options, HttpContext context) =>
        {
            RequireAdministrator(database, context);
            var id = Guid.NewGuid().ToString("N");
            var position = database.Store.CreateBackup(Path.Combine(Path.GetFullPath(options.DataDirectory), "backups", id));
            return new BackupReceipt(id, position);
        });
        app.MapGet("/v1/status", (DatabaseEngine database, IRaftCluster cluster, OrleansNode orleans, HttpContext context) =>
        {
            RequireAdministrator(database, context);
            return new NodeStatus(database.Store.Identity.NodeId.ToString(), database.Store.Identity.Incarnation, database.LastApplied,
                cluster.Leader?.EndPoint.ToString(), cluster.Members.Count, database.Durability, orleans.Grains is not null, Environment.ProcessId);
        });
        return app;
    }
    private static void RequireAdministrator(DatabaseEngine database, HttpContext context)
    {
        if (!database.Store.Read(v => database.Principal(v, Principal(context), DateTimeOffset.UtcNow).ClusterAdministrator))
            throw Errors.Fail(ErrorCode.PermissionDenied, "Cluster administration is required.");
    }
    private static Guid CommandId(HttpContext context) => Guid.TryParse(context.Request.Headers["X-KeyLoad-Command-Id"], out var id) && id != Guid.Empty ? id
        : throw Errors.Fail(ErrorCode.Validation, "A stable X-KeyLoad-Command-Id header is required.");
}
