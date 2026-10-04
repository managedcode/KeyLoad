using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.GraphTraversal;

/// <summary>Resolves vertex visibility once per qualified identity within one read cut.</summary>
internal sealed class GraphVertexVisibility(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
    ReadExecutionBudget budget)
{
    private const string VertexUnavailable = "The graph vertex is unavailable.";
    private readonly Dictionary<EntityRef, bool> visible = [];
    private readonly Dictionary<(PartitionRef Partition, string Collection), bool> collections = [];

    /// <summary>Preserves start-vertex authorization and unavailable errors.</summary>
    internal void RequireStart(EntityRef vertex)
    {
        RequireCollection(vertex);
        if (!ReadVisibility(vertex))
        {
            throw Errors.Fail(ErrorCode.NotFound, VertexUnavailable);
        }
        visible.Add(vertex, true);
    }

    /// <summary>Returns only a boolean; decoded document JSON is never retained.</summary>
    internal bool CanVisit(EntityRef vertex)
    {
        budget.Check();
        if (visible.TryGetValue(vertex, out var allowed))
        {
            return allowed;
        }
        if (!CanReadCollection(vertex))
        {
            return visible[vertex] = false;
        }
        return visible[vertex] = ReadVisibility(vertex);
    }

    private bool CanReadCollection(EntityRef vertex)
    {
        var key = (vertex.Partition, vertex.Collection);
        if (collections.TryGetValue(key, out var allowed))
        {
            return allowed;
        }
        try
        {
            RequireCollection(vertex);
            return true;
        }
        catch (KeyLoadException exception) when (exception.Code is ErrorCode.NotFound or ErrorCode.PermissionDenied)
        {
            collections[key] = false;
            return false;
        }
    }

    private void RequireCollection(EntityRef vertex)
    {
        var key = (vertex.Partition, vertex.Collection);
        if (collections.TryGetValue(key, out var allowed) && allowed)
        {
            return;
        }
        database.Authorization.Require(principal, vertex.Partition, vertex.Collection, Capability.DocumentsRead);
        database.Resource(view, vertex.Partition, vertex.Collection, ResourceKind.Collection);
        collections[key] = true;
    }

    private bool ReadVisibility(EntityRef vertex)
    {
        var document = budget.ReadRecord<DocumentRecord>(view, DocumentStorageKeys.RecordKey(vertex));
        return document is not null && !document.Deleted && database.Authorization.CanReadRow(principal, document.Access);
    }
}
