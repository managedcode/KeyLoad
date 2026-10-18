using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    /// <summary>Reads a bounded retained stream page under fresh authorization and its original traversal cut.</summary>
    /// <param name="principalId">The server-authenticated subject whose current persisted policy is checked.</param>
    /// <param name="request">Original direction, result bounds and optional signed traversal continuation.</param>
    /// <param name="cancellationToken">Original cancellation for the complete read.</param>
    /// <returns>The authorized events and continuation at the captured canonical cut.</returns>
    public StreamPage ReadStream(string principalId, ReadStreamRequest request,
        CancellationToken cancellationToken = default)
        => ReadStreamTraversal(principalId, request, new(OperationLimitsOptions, Clock, cancellationToken));

    internal StreamPage ReadStream(string principalId, ReadStreamRequest request,
        PhysicalShardRecord configuredOwner, CancellationToken cancellationToken = default)
    {
        VerifyStorageOwner(configuredOwner, cancellationToken);
        return ReadStreamTraversal(principalId, request, new(OperationLimitsOptions, Clock, cancellationToken));
    }

    private StreamPage ReadStreamTraversal(string principalId, ReadStreamRequest request, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Stream);
        if (request.AfterRevision < StreamTraversalProtocol.EmptyRevision
            || request.Direction is not (StreamReadDirection.Forward or StreamReadDirection.Backward)
            || request.MaxBytes < StreamTraversalProtocol.UnspecifiedBytes || request.MaxBytes > Limits.MaxBatchBytes
            || request.Cursor is not null && request.AfterRevision != StreamTraversalProtocol.EmptyRevision)
        {
            throw Errors.Fail(ErrorCode.Validation, StreamTraversalProtocol.InvalidRequest);
        }
        ValidateStreamRead(request.AfterRevision, request.Limit);
        budget.ConstrainResultBytes(request.MaxBytes == StreamTraversalProtocol.UnspecifiedBytes
            ? Limits.MaxBatchBytes : request.MaxBytes);
        return Store.Read(view => ReadStreamTraversal(view, principalId, request, budget));
    }

    private StreamPage ReadStreamTraversal(IKeyValueView view, string principalId, ReadStreamRequest request,
        ReadExecutionBudget budget)
    {
        budget.Check();
        var now = Clock.GetUtcNow();
        var bounded = budget.CreateView(view);
        var principal = Principal(bounded, principalId, now);
        var stream = request.Stream;
        Authorization.Require(principal, stream.Partition, stream.StreamSet, Capability.EventsRead);
        var resource = BindAuthorizedReadResource(bounded, stream.Partition, stream.StreamSet, ResourceKind.StreamSet);
        var applied = ReadCanonicalSequence(bounded);
        var head = ReadStreamHead(view, stream, budget);
        ValidateStreamHead(head, stream, request.Cursor is null && request.Direction == StreamReadDirection.Forward
            ? request.AfterRevision : head.FirstAvailableRevision - StreamTraversalProtocol.FirstRevision);
        var cursor = request.Cursor is null
            ? NewStreamTraversal(principal, resource, request, head, now, applied)
            : ReadStreamTraversalCursor(request, principal, resource, head, now, applied);
        var range = ReadTraversalEvents(bounded, stream, principal, resource, cursor, request.Limit, budget);
        var next = range.Events.IsEmpty ? cursor.NextExclusiveRevision
            : range.Events[^StreamTraversalProtocol.LastEventOffset].Revision;
        var token = range.HasMore ? Sign(cursor with { NextExclusiveRevision = next }) : null;
        var page = new StreamPage(stream,
            new(cursor.CapturedTailRevision, cursor.CapturedFirstAvailableRevision, stream.Generation),
            range.Events, applied, range.HasMore, token, cursor.SnapshotCutPosition);
        budget.CheckResult(page);
        return page;
    }
}
