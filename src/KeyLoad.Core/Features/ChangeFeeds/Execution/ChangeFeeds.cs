using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core;

/// <summary>Provides authorized change-feed reads over the database engine.</summary>
public sealed partial class DatabaseEngine
{
    private const string VisibilityEpochSpace = "visibility-epoch";
    private const int InitialVisibilityEpoch = 0;
    private const int AdvanceVisibilityEpochVisibilityEpochStep = 1;

    private const string DocumentChangeFeedCursorPurpose = "document-change-feed";
    private static long VisibilityEpoch(IKeyValueView view, PartitionRef partition, string collection)
        => view.ReadOwnedValue(KeySpace.Partition(VisibilityEpochSpace, partition, collection)) is { } bytes ? NativeSerialization.Deserialize<long>(bytes) : InitialVisibilityEpoch;
    private static void AdvanceVisibilityEpoch(IAtomicTransaction tx, PartitionRef partition, string collection)
        => tx.PutRecord(KeySpace.Partition(VisibilityEpochSpace, partition, collection), checked(VisibilityEpoch(tx, partition, collection) + AdvanceVisibilityEpochVisibilityEpochStep));
    private string ChangeCursor(IKeyValueView view, PrincipalRecord principal, ResourceDefinition resource, PartitionRef partition, long after)
        => Sign(new ChangeFeedClaims(DocumentChangeFeedCursorPurpose, Store.Identity.Incarnation, partition, resource.Name, principal.Id,
            principal.PolicyEpoch, resource.SchemaVersion, VisibilityEpoch(view, partition, resource.Name), after, Clock.GetUtcNow().Add(changeFeedExecution.CursorLifetime)));
    /// <summary>Captures a signed cursor at the current outbox tail for an authorized collection.</summary>
    /// <param name="view">Current gated storage view.</param>
    /// <param name="principal">Persisted authorized principal.</param>
    /// <param name="partition">Atomic partition containing the collection.</param>
    /// <param name="collection">Collection to observe.</param>
    /// <returns>The signed cursor and captured tail.</returns>
    public (string Cursor, long Tail) CaptureChangeFeedCursor(IKeyValueView view, PrincipalRecord principal, PartitionRef partition, string collection)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(collection);
        Authorization.Require(principal, partition, collection, Capability.ChangesRead | Capability.DocumentsRead);
        var resource = Resource(view, partition, collection, ResourceKind.Collection);
        var tail = ReadOutboxHead(view, partition).Tail;
        return (ChangeCursor(view, principal, resource, partition, tail), tail);
    }
    /// <summary>Reads one authorized and projected document-change page.</summary>
    /// <param name="principalId">Persisted principal identifier.</param>
    /// <param name="request">Bounded feed request and optional cursor.</param>
    /// <returns>The ordered page at one storage cut.</returns>
    public ChangeFeedPage ReadChangeFeed(string principalId, ReadChangeFeedRequest request)
        => ReadChangeFeed(principalId, request, CancellationToken.None);

    /// <summary>Reads one same-cut projected change page within the original caller budget.</summary>
    /// <param name="principalId">Persisted principal identifier.</param>
    /// <param name="request">Original bounded feed request and signed cursor.</param>
    /// <param name="cancellationToken">Original caller cancellation across all native work.</param>
    /// <returns>A complete projected page or a failure without a partial page.</returns>
    public ChangeFeedPage ReadChangeFeed(string principalId, ReadChangeFeedRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principalId);
        ArgumentNullException.ThrowIfNull(request);
        var budget = new ReadExecutionBudget(OperationLimitsOptions, Clock, cancellationToken);
        budget.Check();
        return Store.Read<ChangeFeedPage>(original =>
        {
            budget.Check();
            var grant = budget.CreateReadGrant(Limits.MaxQueryReadBytes, Limits.MaxScanRecords);
            var view = budget.CreateView(original, grant);
            var principal = Principal(view, principalId, Clock.GetUtcNow());
            var page = ReadChangeFeedView(view, principal, request, (resource, change) => new DocumentChange(change.Sequence, change.Commit,
                change.CommittedAt, change.After.Reference, change.After.Revision, change.After.Deleted,
                change.Before is { } before ? Project(principal, resource, before) : null,
                change.After.Deleted ? null : Project(principal, resource, change.After)),
                budget.CheckResult);
            var result = new ChangeFeedPage(page.Changes, page.Cursor, page.ThroughSequence, page.Tail,
                page.FirstAvailable, page.HasMore, page.CutPosition);
            budget.CheckResult(result);
            budget.Check();
            return result;
        });
    }
    /// <summary>Projects visible changes inside an existing gated read view.</summary>
    /// <typeparam name="T">Owned projected change type.</typeparam>
    /// <param name="view">Current gated storage view.</param>
    /// <param name="principal">Persisted authorized principal.</param>
    /// <param name="request">Bounded feed request and optional cursor.</param>
    /// <param name="project">Projection applied only to authorized changes.</param>
    /// <returns>Ordered immutable projected changes and continuation metadata.</returns>
    public AuthorizedDocumentChangePage<T> ReadChangeFeedView<T>(IKeyValueView view, PrincipalRecord principal,
        ReadChangeFeedRequest request, Func<ResourceDefinition, AuthorizedDocumentChange, T?> project) where T : class
        => ReadChangeFeedView(view, principal, request, project, beforeRetain: null);

    internal AuthorizedDocumentChangePage<T> ReadChangeFeedView<T>(IKeyValueView view, PrincipalRecord principal,
        ReadChangeFeedRequest request, Func<ResourceDefinition, AuthorizedDocumentChange, T?> project,
        Action<T>? beforeRetain) where T : class
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(project);
        var (resource, head, after) = PrepareChangeRead(view, principal, request);
        return ProjectChangePage(view, principal, request, project, resource, head, after, beforeRetain);
    }

    private (ResourceDefinition Resource, OutboxHead Head, long After) PrepareChangeRead(
        IKeyValueView view, PrincipalRecord principal, ReadChangeFeedRequest request)
    {
        const int LimitValidationBoundary = 1;
        const int MaxBytesValidationBoundary = 1;
        const string InvalidChangeReadBoundsDetail = "The change-feed request exceeds its supported bounds.";
        const int FirstAvailableStep = 1;
        const string FreshAuthorizedSnapshotRequiredDetail = "The change-feed cursor requires a fresh authorized snapshot.";

        ValidatePartition(request.Partition);
        JsonData.Identifier(request.Collection);
        var now = Clock.GetUtcNow();
        Authorization.Require(principal, request.Partition, request.Collection, Capability.ChangesRead | Capability.DocumentsRead);
        var resource = Resource(view, request.Partition, request.Collection, ResourceKind.Collection);
        if (request.Limit < LimitValidationBoundary || request.Limit > Limits.MaxResults || request.MaxBytes < MaxBytesValidationBoundary || request.MaxBytes > Limits.MaxBatchBytes
            || request.Start is not (ChangeFeedStart.Beginning or ChangeFeedStart.Now))
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidChangeReadBoundsDetail);
        }

        var head = ReadOutboxHead(view, request.Partition);
        var after = request.Start == ChangeFeedStart.Now ? head.Tail : head.FirstAvailable - FirstAvailableStep;
        if (request.Cursor is { } cursor)
        {
            var claims = Verify<ChangeFeedClaims>(cursor);
            if (claims.Purpose != DocumentChangeFeedCursorPurpose || claims.Incarnation != Store.Identity.Incarnation || claims.Partition != request.Partition
                || claims.Collection != request.Collection || claims.PrincipalId != principal.Id || claims.PolicyEpoch != principal.PolicyEpoch
                || claims.SchemaVersion != resource.SchemaVersion || claims.VisibilityEpoch != VisibilityEpoch(view, request.Partition, request.Collection)
                || claims.ExpiresAt <= now)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, FreshAuthorizedSnapshotRequiredDetail);
            }

            after = claims.After;
        }
        CheckOutboxPosition(head, after);
        return (resource, head, after);
    }

    private AuthorizedDocumentChangePage<T> ProjectChangePage<T>(IKeyValueView view, PrincipalRecord principal,
        ReadChangeFeedRequest request, Func<ResourceDefinition, AuthorizedDocumentChange, T?> project,
        ResourceDefinition resource, OutboxHead head, long after, Action<T>? beforeRetain) where T : class
    {
        const int BytesInitialValue = 0;
        const string ProjectChangePageDetailText = "The first change exceeds the page byte budget.";

        var changes = new List<T>();
        var through = after;
        var bytes = BytesInitialValue;
        foreach (var entry in ReadOutboxRange(view, request.Partition, after, head.Tail, request.Limit))
        {
            var visible = VisibleChange(view, principal, resource, request.Partition, entry);
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
        return new(changes.ToImmutableArray(), ChangeCursor(view, principal, resource, request.Partition, through), through, head.Tail,
            head.FirstAvailable, through < head.Tail, Store.Position);
    }
    private AuthorizedDocumentChange? VisibleChange(IKeyValueView view, PrincipalRecord principal, ResourceDefinition resource,
        PartitionRef partition, OutboxEntry entry)
    {
        if (entry.Receipt.Resource != resource.Name || entry.After is not { } after)
        {
            return null;
        }

        var current = view.GetRecord<DocumentRecord>(DocumentKey(partition, resource.Name, after.Reference.Id));
        // Historical grants and current row visibility both constrain a historical projection.
        if (current is null || !Authorization.CanReadRow(principal, current.Access) || !Authorization.CanReadRow(principal, after.Access))
        {
            return null;
        }

        var before = entry.Before is { Deleted: false } old && Authorization.CanReadRow(principal, old.Access) ? old : null;
        return new(entry.Sequence, entry.Commit, entry.CommittedAt, before, after);
    }
}
/// <summary>One change whose historical and current row visibility passed authorization.</summary>
/// <param name="Sequence">Outbox sequence.</param>
/// <param name="Commit">Commit token of the source mutation.</param>
/// <param name="CommittedAt">Original commit time.</param>
/// <param name="Before">Visible previous document, when available.</param>
/// <param name="After">Visible document after the mutation.</param>

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.AuthorizedDocumentChange)]
public sealed record AuthorizedDocumentChange(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.AuthorizedDocumentChangeFields.Sequence)] long Sequence,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.AuthorizedDocumentChangeFields.Commit)] CommitToken Commit,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.AuthorizedDocumentChangeFields.CommittedAt)] DateTimeOffset CommittedAt,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.AuthorizedDocumentChangeFields.Before)] DocumentRecord? Before,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.AuthorizedDocumentChangeFields.After)] DocumentRecord After);
/// <summary>Immutable authorized changes and continuation state from one read cut.</summary>
/// <typeparam name="T">Owned projected change type.</typeparam>
/// <param name="Changes">Ordered owned projections.</param>
/// <param name="Cursor">Signed continuation cursor.</param>
/// <param name="ThroughSequence">Last examined outbox sequence.</param>
/// <param name="Tail">Tail of the captured outbox.</param>
/// <param name="FirstAvailable">First retained outbox sequence.</param>
/// <param name="HasMore">Whether later source entries remain.</param>
/// <param name="CutPosition">Storage position represented by this page.</param>

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.AuthorizedDocumentChangePage)]
public sealed record AuthorizedDocumentChangePage<T>(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.AuthorizedDocumentChangePageFields.Changes)] ImmutableArray<T> Changes,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.AuthorizedDocumentChangePageFields.Cursor)] string Cursor,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.AuthorizedDocumentChangePageFields.ThroughSequence)] long ThroughSequence,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.AuthorizedDocumentChangePageFields.Tail)] long Tail,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.AuthorizedDocumentChangePageFields.FirstAvailable)] long FirstAvailable,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.AuthorizedDocumentChangePageFields.HasMore)] bool HasMore,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.AuthorizedDocumentChangePageFields.CutPosition)] long CutPosition);
