using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes StreamTraversalExecution operations against the caller's database context.</summary>
public static class StreamTraversalExecution
{
    /// <summary>Reads a bounded retained stream page under fresh authorization and its original traversal cut.</summary>
    /// <param name = "principalId">The server-authenticated subject whose current persisted policy is checked.</param>
    /// <param name = "request">Original direction, result bounds and optional signed traversal continuation.</param>
    /// <param name = "cancellationToken">Original cancellation for the complete read.</param>
    /// <returns>The authorized events and continuation at the captured canonical cut.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static StreamPage ReadStream(this DatabaseEngine engine, string principalId, ReadStreamRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.ReadStreamTraversal(principalId, request, new(engine.OperationLimitsOptions, engine.Clock, cancellationToken));
    }

    internal static StreamPage ReadStream(this DatabaseEngine engine, string principalId, ReadStreamRequest request, PhysicalShardRecord configuredOwner, CancellationToken cancellationToken = default)
    {
        engine.VerifyStorageOwner(configuredOwner, cancellationToken);
        return engine.ReadStreamTraversal(principalId, request, new(engine.OperationLimitsOptions, engine.Clock, cancellationToken));
    }

    internal static StreamPage ReadStreamTraversal(this DatabaseEngine engine, string principalId, ReadStreamRequest request, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Stream);
        if (request.AfterRevision < StreamTraversalProtocol.EmptyRevision || request.Direction is not (StreamReadDirection.Forward or StreamReadDirection.Backward) || request.MaxBytes < StreamTraversalProtocol.UnspecifiedBytes || request.MaxBytes > engine.Limits.MaxBatchBytes || request.Cursor is not null && request.AfterRevision != StreamTraversalProtocol.EmptyRevision)
        {
            throw Errors.Fail(ErrorCode.Validation, StreamTraversalProtocol.InvalidRequest);
        }

        engine.ValidateStreamRead(request.AfterRevision, request.Limit);
        budget.ConstrainResultBytes(request.MaxBytes == StreamTraversalProtocol.UnspecifiedBytes ? engine.Limits.MaxBatchBytes : request.MaxBytes);
        return engine.Store.Read(view => engine.ReadStreamTraversal(view, principalId, request, budget));
    }

    internal static StreamPage ReadStreamTraversal(this DatabaseEngine engine, IKeyValueView view, string principalId, ReadStreamRequest request, ReadExecutionBudget budget)
    {
        budget.Check();
        var now = engine.Clock.GetUtcNow();
        var bounded = budget.CreateView(view);
        var principal = engine.Principal(bounded, principalId, now);
        var stream = request.Stream;
        engine.Authorization.Require(principal, stream.Partition, stream.StreamSet, Capability.EventsRead);
        var resource = ResourceLifetimeReadBinding.BindAuthorizedReadResource(bounded, stream.Partition, stream.StreamSet, ResourceKind.StreamSet);
        var applied = StorageOperationApply.ReadCanonicalSequence(bounded);
        var head = StreamReadExecution.ReadStreamHead(view, stream, budget);
        StreamReadExecution.ValidateStreamHead(head, stream, request.Cursor is null && request.Direction == StreamReadDirection.Forward ? request.AfterRevision : head.FirstAvailableRevision - StreamTraversalProtocol.FirstRevision);
        var cursor = request.Cursor is null ? engine.NewStreamTraversal(principal, resource, request, head, now, applied) : engine.ReadStreamTraversalCursor(request, principal, resource, head, now, applied);
        var range = engine.ReadTraversalEvents(bounded, stream, principal, resource, cursor, request.Limit, budget);
        var next = range.Events.IsEmpty ? cursor.NextExclusiveRevision : range.Events[^StreamTraversalProtocol.LastEventOffset].Revision;
        var token = range.HasMore ? engine.Sign(cursor with { NextExclusiveRevision = next }) : null;
        var page = new StreamPage(stream, new(cursor.CapturedTailRevision, cursor.CapturedFirstAvailableRevision, stream.Generation), range.Events, applied, range.HasMore, token, cursor.SnapshotCutPosition);
        budget.CheckResult(page);
        return page;
    }
}
