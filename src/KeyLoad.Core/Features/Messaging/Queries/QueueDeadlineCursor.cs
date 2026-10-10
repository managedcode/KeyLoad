using KeyLoad.Storage;

namespace KeyLoad.Core;

internal sealed record QueueDeadlineCursor(Guid Incarnation, long ReadGeneration, int Next,
    DuePrefixCursor Scheduled, DuePrefixCursor Leased, DuePrefixCursor Metadata)
{
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
        var second = validator with { Schedules = previous.Metadata };
        return DueWorkCursor.IsValid(validator) && DueWorkCursor.IsValid(second)
            ? previous with { Scheduled = Copy(previous.Scheduled), Leased = Copy(previous.Leased), Metadata = Copy(previous.Metadata) }
            : Match(identity, null);
    }

    internal DuePrefixCursor Bounds => Next switch
    {
        ScheduledIndex => Scheduled,
        LeasedIndex => Leased,
        _ => Metadata
    };

    internal QueueDeadlineCursor Advance(DuePrefixCursor bounds) => Next switch
    {
        ScheduledIndex => this with { Scheduled = Copy(bounds), Next = LeasedIndex },
        LeasedIndex => this with { Leased = Copy(bounds), Next = MetadataIndex },
        _ => this with { Metadata = Copy(bounds), Next = ScheduledIndex }
    };

    internal string Space => Next switch
    {
        ScheduledIndex => QueueDeadlineRecordDecoder.Scheduled,
        LeasedIndex => QueueDeadlineRecordDecoder.Leased,
        _ => QueueDeadlineRecordDecoder.Metadata
    };

    private static DuePrefixCursor Copy(DuePrefixCursor value)
        => value with { UpperKey = value.UpperKey?.ToArray(), LastKey = value.LastKey?.ToArray() };
}
