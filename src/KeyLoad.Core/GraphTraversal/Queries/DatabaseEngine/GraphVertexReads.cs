using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes GraphVertexReads operations against the caller's database context.</summary>
public static class GraphVertexReads
{
    internal static DocumentRecord VisibleVertex(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, EntityRef vertex, ReadExecutionBudget? budget = null)
    {
        const string VertexUnavailable = "The graph vertex is unavailable.";
        const string VertexMismatch = "A canonical graph vertex record is inconsistent.";
        engine.Authorization.Require(principal, vertex.Partition, vertex.Collection, Capability.DocumentsRead);
        var definition = view is IResourceLifetimeReadBindingView ? ResourceLifetimeReadBinding.BindAuthorizedReadResource(view, vertex.Partition, vertex.Collection, ResourceKind.Collection) : engine.Resource(view, vertex.Partition, vertex.Collection, ResourceKind.Collection);
        var metadata = budget is null ? view : budget.CreateView(view);
        vertex = EntityReferenceLifetime.Bind(metadata, vertex, definition);
        var key = DocumentStorageExecutionDocuments.DocumentKey(vertex.Partition, vertex.Collection, vertex.Id);
        var document = budget is null ? view.GetRecord<DocumentRecord>(key) : budget.ReadRecord<DocumentRecord>(view, key);
        if (document is null || document.Deleted || !engine.Authorization.CanReadRow(principal, document.Access))
        {
            throw Errors.Fail(ErrorCode.NotFound, VertexUnavailable);
        }

        if (document.Reference is null || document.Reference.Partition != vertex.Partition || document.Reference.Collection != vertex.Collection || document.Reference.Id != vertex.Id)
        {
            throw Errors.Fail(ErrorCode.Corruption, VertexMismatch);
        }

        EntityReferenceLifetime.RequireStored(metadata, document.Reference, definition);
        if (document.Reference != vertex)
        {
            throw Errors.Fail(ErrorCode.Corruption, VertexMismatch);
        }

        return document;
    }
}
