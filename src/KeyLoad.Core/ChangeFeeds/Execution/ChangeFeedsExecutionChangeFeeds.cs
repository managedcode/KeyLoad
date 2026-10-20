using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes ChangeFeedsExecutionChangeFeeds operations against the caller's database context.</summary>
public static class ChangeFeedsExecutionChangeFeeds
{
    internal const string VisibilityEpochSpace = "visibility-epoch";
    internal const int InitialVisibilityEpoch = 0;
    internal const int AdvanceVisibilityEpochVisibilityEpochStep = 1;
    internal const string DocumentChangeFeedCursorPurpose = "document-change-feed";
    internal static long VisibilityEpoch(IKeyValueView view, PartitionRef partition, string collection) => view.ReadOwnedValue(KeySpace.Partition(VisibilityEpochSpace, partition, collection)) is { } bytes ? OrleansSerialization.Deserialize<long>(bytes) : InitialVisibilityEpoch;
    internal static void AdvanceVisibilityEpoch(IAtomicTransaction tx, PartitionRef partition, string collection) => tx.PutRecord(KeySpace.Partition(VisibilityEpochSpace, partition, collection), checked(VisibilityEpoch(tx, partition, collection) + AdvanceVisibilityEpochVisibilityEpochStep));
    internal static string ChangeCursor(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, ResourceDefinition resource, PartitionRef partition, long after) => engine.Sign(new ChangeFeedClaims(DocumentChangeFeedCursorPurpose, engine.Store.Identity.Incarnation, partition, resource.Name, principal.Id, principal.PolicyEpoch, resource.SchemaVersion, VisibilityEpoch(view, partition, resource.Name), after, engine.Clock.GetUtcNow().Add(engine.ChangeFeedExecution.CursorLifetime)));
    /// <summary>Captures a signed cursor at the current outbox tail for an authorized collection.</summary>
    /// <param name = "view">Current gated storage view.</param>
    /// <param name = "principal">Persisted authorized principal.</param>
    /// <param name = "partition">Atomic partition containing the collection.</param>
    /// <param name = "collection">Collection to observe.</param>
    /// <returns>The signed cursor and captured tail.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static (string Cursor, long Tail) CaptureChangeFeedCursor(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, string collection)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(collection);
        engine.Authorization.Require(principal, partition, collection, Capability.ChangesRead | Capability.DocumentsRead);
        var resource = ResourceExecutionQueriesResourceLifetimeReadBinding.BindAuthorizedReadResource(view, partition, collection, ResourceKind.Collection);
        var tail = engine.ReadOutboxHead(view, partition).Tail;
        return (engine.ChangeCursor(view, principal, resource, partition, tail), tail);
    }

    /// <summary>Reads one authorized and projected document-change page.</summary>
    /// <param name = "principalId">Persisted principal identifier.</param>
    /// <param name = "request">Bounded feed request and optional cursor.</param>
    /// <returns>The ordered page at one storage cut.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static ChangeFeedPage ReadChangeFeed(this DatabaseEngine engine, string principalId, ReadChangeFeedRequest request)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.ReadChangeFeed(principalId, request, CancellationToken.None);
    }

    /// <summary>Reads one same-cut projected change page within the original caller budget.</summary>
    /// <param name = "principalId">Persisted principal identifier.</param>
    /// <param name = "request">Original bounded feed request and signed cursor.</param>
    /// <param name = "cancellationToken">Original caller cancellation across all execution work.</param>
    /// <returns>A complete projected page or a failure without a partial page.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static ChangeFeedPage ReadChangeFeed(this DatabaseEngine engine, string principalId, ReadChangeFeedRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(principalId);
        ArgumentNullException.ThrowIfNull(request);
        var budget = new ReadExecutionBudget(engine.OperationLimitsOptions, engine.Clock, cancellationToken);
        return engine.ReadChangeFeedRead(principalId, request, budget);
    }

    internal static ChangeFeedPage ReadChangeFeedRead(this DatabaseEngine engine, string principalId, ReadChangeFeedRequest request, ReadExecutionBudget budget)
    {
        budget.Check();
        return engine.Store.Read<ChangeFeedPage>(original =>
        {
            budget.Check();
            var grant = budget.CreateReadGrant(engine.Limits.MaxQueryReadBytes, engine.Limits.MaxScanRecords);
            var view = budget.CreateView(original, grant);
            var principal = engine.Principal(view, principalId, engine.Clock.GetUtcNow());
            var page = engine.ReadChangeFeedView(view, principal, request, (resource, change) => new DocumentChange(change.Sequence, change.Commit, change.CommittedAt, change.After.Reference, change.After.Revision, change.After.Deleted, change.Before is { } before ? engine.Project(principal, resource, before) : null, change.After.Deleted ? null : engine.Project(principal, resource, change.After)), budget.CheckResult);
            var result = new ChangeFeedPage(page.Changes, page.Cursor, page.ThroughSequence, page.Tail, page.FirstAvailable, page.HasMore, page.CutPosition);
            budget.CheckResult(result);
            budget.Check();
            return result;
        });
    }

    /// <summary>Projects visible changes inside an existing gated read view.</summary>
    /// <typeparam name = "T">Owned projected change type.</typeparam>
    /// <param name = "view">Current gated storage view.</param>
    /// <param name = "principal">Persisted authorized principal.</param>
    /// <param name = "request">Bounded feed request and optional cursor.</param>
    /// <param name = "project">Projection applied only to authorized changes.</param>
    /// <returns>Ordered immutable projected changes and continuation metadata.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static AuthorizedDocumentChangePage<T> ReadChangeFeedView<T>(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, ReadChangeFeedRequest request, Func<ResourceDefinition, AuthorizedDocumentChange, T?> project)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.ReadChangeFeedView(view, principal, request, project, beforeRetain: null);
    }

    internal static AuthorizedDocumentChangePage<T> ReadChangeFeedView<T>(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, ReadChangeFeedRequest request, Func<ResourceDefinition, AuthorizedDocumentChange, T?> project, Action<T>? beforeRetain)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(project);
        var (resource, head, after) = engine.PrepareChangeRead(view, principal, request);
        return engine.ProjectChangePage(view, principal, request, project, resource, head, after, beforeRetain);
    }

    internal static (ResourceDefinition Resource, OutboxHead Head, long After) PrepareChangeRead(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, ReadChangeFeedRequest request)
    {
        const int LimitValidationBoundary = 1;
        const int MaxBytesValidationBoundary = 1;
        const string InvalidChangeReadBoundsDetail = "The change-feed request exceeds its supported bounds.";
        const int FirstAvailableStep = 1;
        const string FreshAuthorizedSnapshotRequiredDetail = "The change-feed cursor requires a fresh authorized snapshot.";
        EngineResourceAccess.ValidatePartition(request.Partition);
        JsonData.Identifier(request.Collection);
        var now = engine.Clock.GetUtcNow();
        engine.Authorization.Require(principal, request.Partition, request.Collection, Capability.ChangesRead | Capability.DocumentsRead);
        var resource = ResourceExecutionQueriesResourceLifetimeReadBinding.BindAuthorizedReadResource(view, request.Partition, request.Collection, ResourceKind.Collection);
        if (request.Limit < LimitValidationBoundary || request.Limit > engine.Limits.MaxResults || request.MaxBytes < MaxBytesValidationBoundary || request.MaxBytes > engine.Limits.MaxBatchBytes || request.Start is not (ChangeFeedStart.Beginning or ChangeFeedStart.Now))
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidChangeReadBoundsDetail);
        }

        var head = engine.ReadOutboxHead(view, request.Partition);
        var after = request.Start == ChangeFeedStart.Now ? head.Tail : head.FirstAvailable - FirstAvailableStep;
        if (request.Cursor is { } cursor)
        {
            var claims = engine.Verify<ChangeFeedClaims>(cursor);
            if (claims.Purpose != DocumentChangeFeedCursorPurpose || claims.Incarnation != engine.Store.Identity.Incarnation || claims.Partition != request.Partition || claims.Collection != request.Collection || claims.PrincipalId != principal.Id || claims.PolicyEpoch != principal.PolicyEpoch || claims.SchemaVersion != resource.SchemaVersion || claims.VisibilityEpoch != VisibilityEpoch(view, request.Partition, request.Collection) || claims.ExpiresAt <= now)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, FreshAuthorizedSnapshotRequiredDetail);
            }

            after = claims.After;
        }

        ChangeFeedsExecutionProjectionOutbox.CheckOutboxPosition(head, after);
        return (resource, head, after);
    }

    internal static AuthorizedDocumentChangePage<T> ProjectChangePage<T>(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, ReadChangeFeedRequest request, Func<ResourceDefinition, AuthorizedDocumentChange, T?> project, ResourceDefinition resource, OutboxHead head, long after, Action<T>? beforeRetain)
        where T : class
    {
        const int BytesInitialValue = 0;
        const string ProjectChangePageDetailText = "The first change exceeds the page byte budget.";
        var changes = new List<T>();
        var through = after;
        var bytes = BytesInitialValue;
        foreach (var entry in ChangeFeedsExecutionProjectionOutbox.ReadOutboxRange(view, request.Partition, after, head.Tail, request.Limit))
        {
            var visible = engine.VisibleChange(view, principal, resource, request.Partition, entry);
            var change = visible is null ? null : project(resource, visible);
            if (change is null)
            {
                through = entry.Sequence;
                continue;
            }

            var size = JsonDefaults.Serialize(change).Length;
            if (size > request.MaxBytes - bytes)
            {
                if (through == after)
                {
                    throw Errors.Fail(ErrorCode.BudgetExceeded, ProjectChangePageDetailText);
                }

                break;
            }

            beforeRetain?.Invoke(change);
            changes.Add(change);
            bytes += size;
            through = entry.Sequence;
        }

        return new(changes.ToImmutableArray(), engine.ChangeCursor(view, principal, resource, request.Partition, through), through, head.Tail, head.FirstAvailable, through < head.Tail, engine.Store.Position);
    }

    internal static AuthorizedDocumentChange? VisibleChange(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, ResourceDefinition resource, PartitionRef partition, OutboxEntry entry)
    {
        if (entry.Receipt.Resource != resource.Name || entry.After is not { } after)
        {
            return null;
        }

        if (after.Reference.Partition != partition || after.Reference.Collection != resource.Name || after.Reference.Id != entry.Receipt.Id || entry.Before is { } beforeIdentity && beforeIdentity.Reference != after.Reference)
        {
            const string HistoricalIdentityMismatch = "The committed document change does not match its canonical identity.";
            throw Errors.Fail(ErrorCode.Corruption, HistoricalIdentityMismatch);
        }

        var current = view.GetRecord<DocumentRecord>(DocumentStorageExecutionDocuments.DocumentKey(partition, resource.Name, after.Reference.Id));
        if (current is not null && (current.Reference.Partition != partition || current.Reference.Collection != resource.Name || current.Reference.Id != after.Reference.Id))
        {
            const string CurrentIdentityMismatch = "The change-feed current document does not match its canonical identity.";
            throw Errors.Fail(ErrorCode.Corruption, CurrentIdentityMismatch);
        }

        // Historical grants and current row visibility both constrain a historical projection.
        if (current is null || !engine.Authorization.CanReadRow(principal, current.Access) || !engine.Authorization.CanReadRow(principal, after.Access))
        {
            return null;
        }

        var before = entry.Before is { Deleted: false } old && engine.Authorization.CanReadRow(principal, old.Access) ? old : null;
        return new(entry.Sequence, entry.Commit, entry.CommittedAt, before, after);
    }
}
