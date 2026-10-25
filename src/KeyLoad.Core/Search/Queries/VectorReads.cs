using KeyLoad.Core.Search;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes VectorReads operations against the caller's database context.</summary>
public static class VectorReads
{
    /// <summary>Runs an authorized callback over visible vectors at one storage cut.</summary>
    /// <typeparam name = "T">Owned callback result type.</typeparam>
    /// <param name = "principalId">Persisted principal identifier.</param>
    /// <param name = "partition">Owning atomic partition.</param>
    /// <param name = "collection">Document collection.</param>
    /// <param name = "field">Authorized vector field.</param>
    /// <param name = "read">Callback over visible document-vector pairs.</param>
    /// <returns>The callback's owned result.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static T WithVectors<T>(this DatabaseEngine engine, string principalId, PartitionRef partition, string collection, string field, Func<PrincipalRecord, ResourceDefinition, (DocumentRecord Document, VectorRecord Vector)[], T> read)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.Store.Read(view =>
        {
            var principal = engine.Principal(view, principalId, engine.Clock.GetUtcNow());
            engine.Authorization.Require(principal, partition, collection, Capability.VectorSearch);
            var resource = engine.Resource(view, partition, collection, ResourceKind.Collection);
            engine.Authorization.RequireFieldUse(principal, resource, field);
            return read(principal, resource, engine.ReadVisibleVectors(view, principal, partition, collection, field));
        });
    }

    /// <summary>Materializes visible revision-matching vector pairs under a shared budget.</summary>
    /// <param name = "view">Current gated storage view.</param>
    /// <param name = "principal">Persisted principal.</param>
    /// <param name = "partition">Owning atomic partition.</param>
    /// <param name = "collection">Document collection.</param>
    /// <param name = "field">Authorized vector field.</param>
    /// <param name = "budget">Shared read budget, or the configured default.</param>
    /// <returns>Owned visible document-vector pairs.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static (DocumentRecord Document, VectorRecord Vector)[] ReadVisibleVectors(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, string collection, string field, ReadExecutionBudget? budget = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        budget ??= new(timeProvider: engine.Clock);
        var eligible = new List<(DocumentRecord, VectorRecord)>();
        engine.VisitVisibleVectors(view, principal, partition, collection, field, budget, (document, vector) => eligible.Add((document, vector)));
        return eligible.ToArray();
    }

    /// <summary>Visits visible revision-matching vector pairs inside the existing read cut.</summary>
    /// <param name = "view">The gate-scoped read view.</param>
    /// <param name = "principal">Persisted, verified principal.</param>
    /// <param name = "partition">Owning atomic partition.</param>
    /// <param name = "collection">Configured collection.</param>
    /// <param name = "field">Authorized vector field.</param>
    /// <param name = "budget">Shared cancellation and cumulative resource limits.</param>
    /// <param name = "visitor">Synchronous callback; it cannot escape or mutate the read view.</param>

    /// <param name = "engine">The shared database execution context.</param>
    public static void VisitVisibleVectors(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, string collection, string field, ReadExecutionBudget budget, Action<DocumentRecord, VectorRecord> visitor)
    {
        ArgumentNullException.ThrowIfNull(engine);
        VisibleVectorReads.Visit(engine, view, principal, partition, collection, field, budget, visitor);
    }
}
