using KeyLoad.Storage;

namespace KeyLoad.Core;

/// <summary>Resumes the actual scheduled, leased and metadata discovery prefixes.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(QueueDeadlineCursor.SerializerAlias)]
public sealed record QueueDeadlineCursor([property: Orleans.Id(0)] Guid Incarnation,
    [property: Orleans.Id(1)] long ReadGeneration,
    [property: Orleans.Id(2)] int Next,
    [property: Orleans.Id(3)] DuePrefixCursor Scheduled,
    [property: Orleans.Id(4)] DuePrefixCursor Leased,
    [property: Orleans.Id(5)] DuePrefixCursor Metadata)
{
    internal const string SerializerAlias = "keyload.core.queue-deadline-cursor";

    internal const int ScheduledIndex = 0;
    internal const int LeasedIndex = 1;
    internal const int MetadataIndex = 2;

    internal static QueueDeadlineCursor Match(StoreIdentity identity, QueueDeadlineCursor? previous)
    {
        if (previous is null || previous.Incarnation != identity.Incarnation
            || previous.ReadGeneration != identity.ReadGeneration || previous.Next is < ScheduledIndex or > MetadataIndex)
        {
            return new(identity.Incarnation, identity.ReadGeneration, ScheduledIndex,
            DuePrefixCursor.Empty, DuePrefixCursor.Empty, DuePrefixCursor.Empty);
        }
        var validator = new DueSweepCursor(previous.Incarnation, previous.ReadGeneration,
            Features.Messaging.DueWorkKind.Schedule, previous.Scheduled, previous.Leased);
        var second = validator with
        {
            Schedules = previous.Metadata
        };
        return DueWorkCursor.IsValid(validator) && DueWorkCursor.IsValid(second)
            ? previous
            : Match(identity, null);
    }

    internal static QueueDeadlineCursor DeferNext(StoreIdentity identity, QueueDeadlineCursor? previous)
    {
        var cursor = Match(identity, previous);
        return cursor.Advance(cursor.Bounds);
    }

    internal DuePrefixCursor Bounds => Next switch
    {
        ScheduledIndex => Scheduled,
        LeasedIndex => Leased,
        _ => Metadata
    };

    internal QueueDeadlineCursor Advance(DuePrefixCursor bounds) => Next switch
    {
        ScheduledIndex => this with { Scheduled = bounds, Next = LeasedIndex },
        LeasedIndex => this with { Leased = bounds, Next = MetadataIndex },
        _ => this with { Metadata = bounds, Next = ScheduledIndex }
    };

    internal string Space => Next switch
    {
        ScheduledIndex => QueueDeadlineRecordDecoder.Scheduled,
        LeasedIndex => QueueDeadlineRecordDecoder.Leased,
        _ => QueueDeadlineRecordDecoder.Metadata
    };

}
