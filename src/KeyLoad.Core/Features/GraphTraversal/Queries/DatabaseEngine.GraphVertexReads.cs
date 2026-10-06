using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private DocumentRecord VisibleVertex(IKeyValueView view, PrincipalRecord principal, EntityRef vertex, ReadExecutionBudget? budget = null)
    {
        const string VertexUnavailable = "The graph vertex is unavailable.";

        Authorization.Require(principal, vertex.Partition, vertex.Collection, Capability.DocumentsRead);
        Resource(view, vertex.Partition, vertex.Collection, ResourceKind.Collection);
        var key = DocumentKey(vertex.Partition, vertex.Collection, vertex.Id);
        var document = budget is null ? view.GetRecord<DocumentRecord>(key) : budget.ReadRecord<DocumentRecord>(view, key);
        if (document is null || document.Deleted || !Authorization.CanReadRow(principal, document.Access))
        {
            throw Errors.Fail(ErrorCode.NotFound, VertexUnavailable);
        }

        return document;
    }
}
