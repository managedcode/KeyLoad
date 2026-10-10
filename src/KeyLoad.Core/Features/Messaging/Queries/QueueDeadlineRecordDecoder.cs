using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class QueueDeadlineRecordDecoder
{
    internal const string Scheduled = "scheduled";
    internal const string Leased = "lease";
    internal const string Metadata = "message-meta";
    private const int SpaceField = 0;
    private const int TenantField = 1;
    private const int DatabaseField = 2;
    private const int DomainField = 3;
    private const int PartitionField = 4;
    private const int QueueField = 5;
    private const int DeadlineField = 6;
    private const int MetadataFields = 7;
    private const int IndexFields = 8;
    private const int LastField = 1;
    private const int Initial = 0;
    private const int PointRecordCount = 1;
    private const int IndexedReadWithLookahead = 3;

    internal static QueueDeadlineHint? Read(DatabaseEngine database, IKeyValueView view,
        ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, DuePageState page)
    {
        var parts = KeyCodec.Decode(key);
        if (parts.Length is not (MetadataFields or IndexFields)
            || parts[SpaceField] is not string space
            || space is not (Scheduled or Leased or Metadata)
            || parts[TenantField] is not string tenant || parts[DatabaseField] is not string db
            || parts[DomainField] is not string domain || parts[PartitionField] is not string partition
            || parts[QueueField] is not string queue || parts[^LastField] is not string id)
        { throw Errors.Fail(ErrorCode.Corruption, DueWorkProtocol.InvalidRecord); }
        var lane = new QueueLaneRef(new(tenant, db, domain, partition), queue);
        DatabaseEngine.ValidatePartition(lane.Partition);
        JsonData.Identifier(queue);
        JsonData.Identifier(id);
        var metadata = ReadMetadata(database, view, lane, id, space, value, page);
        if (metadata.Id != id || metadata.StateVersion <= Initial || metadata.LeaseVersion < Initial)
        { throw Errors.Fail(ErrorCode.Corruption, DueWorkProtocol.InvalidRecord); }
        if (space == Metadata && parts.Length != MetadataFields
            || space != Metadata && (parts.Length != IndexFields || parts[DeadlineField] is not DateTimeOffset))
        { throw Errors.Fail(ErrorCode.Corruption, DueWorkProtocol.InvalidRecord); }
        if (space != Metadata)
        {
            var expected = space == Leased ? metadata.LeaseUntil : metadata.NotBefore;
            if (expected is null || parts[DeadlineField] is not DateTimeOffset indexed
                || indexed != expected)
            { throw Errors.Fail(ErrorCode.Corruption, DueWorkProtocol.InvalidRecord); }
        }
        return Select(lane, metadata, space, page.WakeAt,
            page.MaximumRecordsPerPage < IndexedReadWithLookahead);
    }

    private static MessageMetadata ReadMetadata(DatabaseEngine database, IKeyValueView view, QueueLaneRef lane,
        string id, string space, ReadOnlySpan<byte> value, DuePageState page)
    {
        if (space == Metadata)
        { return NativeSerialization.Deserialize<MessageMetadata>(value); }
        if (NativeSerialization.Deserialize<string>(value) != id)
        { throw Errors.Fail(ErrorCode.Corruption, DueWorkProtocol.InvalidRecord); }
        var key = KeySpace.Partition(Metadata, lane.Partition, lane.Queue, id);
        page.Check();
        page.ExamineRecord();
        MessageMetadata? metadata = null;
        var found = view.VisitRange(key, PointRecordCount, (actualKey, bytes) =>
        {
            page.Check();
            if (!actualKey.SequenceEqual(key))
            { throw Errors.Fail(ErrorCode.Corruption, DueWorkProtocol.InvalidRecord); }
            if (bytes.Length > database.Limits.MaxBatchBytes)
            { throw Errors.Fail(ErrorCode.ResourceExhausted, DueWorkProtocol.RangeBytesExceeded); }
            metadata = NativeSerialization.Deserialize<MessageMetadata>(bytes);
            return false;
        }, observer: page.ObserveBytes, cancellationToken: page.CancellationToken);
        if (found.HasMore)
        {
            page.ExamineRecord();
            throw Errors.Fail(ErrorCode.Corruption, DueWorkProtocol.InvalidRecord);
        }
        return metadata ?? throw Errors.Fail(ErrorCode.Corruption, DueWorkProtocol.InvalidRecord);
    }

    private static QueueDeadlineHint? Select(QueueLaneRef lane, MessageMetadata metadata, string space,
        DateTimeOffset now, bool metadataDeadlineFallback)
    {
        if (metadata.State is not (MessageState.Scheduled or MessageState.Ready or MessageState.Leased))
        { return null; }
        if (space == Metadata)
        {
            if (metadata.ExpiresAt is { } expires && expires <= now)
            { return Create(lane, metadata, QueueDeadlineKind.ExpireMessage, expires); }
            if (!metadataDeadlineFallback)
            { return null; }
            if (metadata.State == MessageState.Scheduled && metadata.NotBefore is { } scheduled && scheduled <= now)
            { return Create(lane, metadata, QueueDeadlineKind.PromoteScheduled, scheduled); }
            return metadata.State == MessageState.Leased && metadata.LeaseUntil is { } lease && lease <= now
                ? Create(lane, metadata, QueueDeadlineKind.ExpireLease, lease) : null;
        }
        var leased = space == Leased;
        var state = leased ? MessageState.Leased : MessageState.Scheduled;
        var due = leased ? metadata.LeaseUntil : metadata.NotBefore;
        if (metadata.State != state || due is null)
        { throw Errors.Fail(ErrorCode.Corruption, DueWorkProtocol.InvalidRecord); }
        return due <= now ? Create(lane, metadata,
            leased ? QueueDeadlineKind.ExpireLease : QueueDeadlineKind.PromoteScheduled, due.Value) : null;
    }

    private static QueueDeadlineHint Create(QueueLaneRef lane, MessageMetadata metadata,
        QueueDeadlineKind kind, DateTimeOffset deadline)
        => new(lane, new(lane.Queue, metadata.Id, metadata.State, metadata.StateVersion,
            metadata.LeaseVersion, deadline, kind));
}
