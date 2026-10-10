using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static void ValidateQueueDeadlineShape(AdvanceQueueDeadline request)
    {
        JsonData.Identifier(request.MessageId);
        if (request.ExpectedStateVersion <= QueueDeadlineProtocol.Initial
            || request.ExpectedLeaseVersion < QueueDeadlineProtocol.Initial
            || request.ExpectedDeadline.Offset != TimeSpan.Zero || !Enum.IsDefined(request.Kind)
            || request.ExpectedState is not (MessageState.Scheduled or MessageState.Ready or MessageState.Leased)
            || request.Kind == QueueDeadlineKind.PromoteScheduled && request.ExpectedState != MessageState.Scheduled
            || request.Kind == QueueDeadlineKind.ExpireLease && request.ExpectedState != MessageState.Leased)
        { throw Errors.Fail(ErrorCode.Validation, QueueDeadlineProtocol.InvalidShape); }
    }

    private static MessageMetadata RequireQueueDeadlineState(IKeyValueView view, QueueLaneRef lane,
        AdvanceQueueDeadline request, DateTimeOffset now)
    {
        var metadata = view.GetRecord<MessageMetadata>(QueueKey(MessageMetadataSpace, lane, request.MessageId))
            ?? throw Errors.Fail(ErrorCode.NotFound, QueueLifecycleProtocol.MissingMetadata);
        var deadline = request.Kind switch
        {
            QueueDeadlineKind.PromoteScheduled => metadata.NotBefore,
            QueueDeadlineKind.ExpireLease => metadata.LeaseUntil,
            QueueDeadlineKind.ExpireMessage => metadata.ExpiresAt,
            _ => throw Errors.Fail(ErrorCode.Validation, QueueDeadlineProtocol.InvalidShape)
        };
        if (metadata.Id != request.MessageId || metadata.State != request.ExpectedState
            || metadata.StateVersion != request.ExpectedStateVersion || metadata.LeaseVersion != request.ExpectedLeaseVersion
            || deadline != request.ExpectedDeadline)
        { throw Errors.Fail(ErrorCode.RevisionConflict, QueueDeadlineProtocol.StaleCondition); }
        if (request.ExpectedDeadline > now)
        { throw Errors.Fail(ErrorCode.Conflict, QueueDeadlineProtocol.NotDue); }
        return metadata;
    }

}
