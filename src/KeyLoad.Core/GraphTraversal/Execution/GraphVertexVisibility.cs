using KeyLoad.Core.Authorization;
using KeyLoad.Core.DocumentStorage;
using KeyLoad.Storage;

namespace KeyLoad.Core.GraphTraversal;

/// <summary>Resolves vertex visibility once per qualified identity within one read cut.</summary>
internal sealed class GraphVertexVisibility(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
    ReadExecutionBudget budget, Action<EntityRef>? admitVertex = null,
    Action<PartitionRef, string>? admitCollection = null)
{
    private const int InitialSchemaVersion = 0;
    private readonly record struct CollectionDecision(bool Allowed, long SchemaVersion);
    private const string VertexUnavailable = "The graph vertex is unavailable.";
    private const string VertexMismatch = "A canonical graph vertex record is inconsistent.";
    private readonly Dictionary<EntityRef, bool> visible = [];
    private readonly Dictionary<(PartitionRef Partition, string Collection), CollectionDecision> collections = [];

    /// <summary>Preserves start-vertex authorization and unavailable errors.</summary>
    internal EntityRef RequireStart(EntityRef vertex)
    {
        RequireCollection(vertex);
        vertex = database.BindEntityReference(budget.CreateView(view), vertex);
        if (!ReadVisibility(vertex))
        {
            throw Errors.Fail(ErrorCode.NotFound, VertexUnavailable);
        }
        admitVertex?.Invoke(vertex);
        visible[vertex] = true;
        return vertex;
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
            return RememberVertex(vertex, false);
        }
        database.RequireStoredEntityReference(budget.CreateView(view), vertex);
        return RememberVertex(vertex, ReadVisibility(vertex));
    }

    internal bool MatchesTarget(EntityRef admitted, EntityRef requested)
    {
        if (admitted.Partition != requested.Partition || admitted.Collection != requested.Collection
            || admitted.Id != requested.Id)
        {
            return false;
        }
        return database.BindEntityReference(budget.CreateView(view), requested) == admitted;
    }

    private bool CanReadCollection(EntityRef vertex)
    {
        var key = (vertex.Partition, vertex.Collection);
        if (collections.TryGetValue(key, out var allowed))
        {
            return allowed.Allowed;
        }
        try
        {
            RequireCollection(vertex);
            return true;
        }
        catch (KeyLoadException exception) when (exception.Code is ErrorCode.NotFound or ErrorCode.PermissionDenied)
        {
            admitCollection?.Invoke(vertex.Partition, vertex.Collection);
            collections[key] = new(false, InitialSchemaVersion);
            return false;
        }
    }

    private void RequireCollection(EntityRef vertex)
    {
        var key = (vertex.Partition, vertex.Collection);
        if (collections.TryGetValue(key, out var allowed) && allowed.Allowed)
        {
            return;
        }
        database.Authorization.Require(principal, vertex.Partition, vertex.Collection, Capability.DocumentsRead);
        var resourceView = budget.CreateView(view);
        var resource = ResourceLifetimeReadBinding.BindAuthorizedReadResource(resourceView, vertex.Partition, vertex.Collection, ResourceKind.Collection);
        admitCollection?.Invoke(vertex.Partition, vertex.Collection);
        collections[key] = new(true, resource.SchemaVersion);
    }

    private bool RememberVertex(EntityRef vertex, bool allowed)
    {
        admitVertex?.Invoke(vertex);
        visible[vertex] = allowed;
        return allowed;
    }

    private bool ReadVisibility(EntityRef vertex)
    {
        var schema = collections[(vertex.Partition, vertex.Collection)].SchemaVersion;
        RowVisibilityLease? lease = null;
        var allowed = false;
        Exception? primary = null;
        try
        {
            try
            {
                lease = database.RowVisibility?.AcquireCurrent(database.CaptureCurrentRowVisibility(principal, schema, vertex.Partition, vertex.Collection));
                if (lease is null || lease.Contains(vertex.Id))
                {
                    allowed = ReadVisibleDocument(vertex);
                }
            }
            catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
            {
                primary = error;
                throw;
            }
            catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
            {
                primary = error;
                throw;
            }
            finally { lease?.Dispose(); }
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
        {
            RowVisibilityFailureLedger.ThrowJoined(primary, error);
            throw;
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
        {
            RowVisibilityFailureLedger.ThrowJoined(primary, error);
            throw;
        }
        return allowed;
    }

    private bool ReadVisibleDocument(EntityRef vertex)
    {
        var document = budget.ReadRecord<DocumentRecord>(view, DocumentStorageKeys.RecordKey(vertex));
        if (document is null || document.Deleted || !database.Authorization.CanReadRow(principal, document.Access))
        {
            return false;
        }
        if (document.Reference is null || document.Reference.Partition != vertex.Partition
            || document.Reference.Collection != vertex.Collection || document.Reference.Id != vertex.Id)
        {
            throw Errors.Fail(ErrorCode.Corruption, VertexMismatch);
        }
        database.RequireStoredEntityReference(budget.CreateView(view), document.Reference);
        if (document.Reference != vertex)
        {
            throw Errors.Fail(ErrorCode.Corruption, VertexMismatch);
        }
        return true;
    }
}
