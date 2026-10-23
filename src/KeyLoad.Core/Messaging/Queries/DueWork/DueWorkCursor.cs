using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class DueWorkCursor
{
    internal static DueSweepCursor Match(StoreIdentity identity, DueSweepCursor? supplied)
    {
        if (supplied is null || supplied.Incarnation != identity.Incarnation
            || supplied.ReadGeneration != identity.ReadGeneration || !IsValid(supplied))
        {
            return DueSweepCursor.Empty(identity.Incarnation, identity.ReadGeneration);
        }
        return supplied;
    }

    internal static bool IsValid(DueSweepCursor cursor)
        => Enum.IsDefined(cursor.NextPrefix) && Valid(cursor.Schedules) && Valid(cursor.Sagas);

    internal static DueSweepCursor WithPrefix(DueSweepCursor cursor, DueWorkKind kind,
        DuePrefixCursor prefix)
        => kind == DueWorkKind.Schedule
            ? cursor with
            {
                Schedules = prefix,
                NextPrefix = DueWorkKind.Saga
            }
            : cursor with
            {
                Sagas = prefix,
                NextPrefix = DueWorkKind.Schedule
            };

    internal static DuePrefixCursor GetPrefix(DueSweepCursor cursor, DueWorkKind kind)
        => kind == DueWorkKind.Schedule ? cursor.Schedules : cursor.Sagas;

    internal static DueSweepCursor Complete(DueSweepCursor cursor, DueWorkKind kind)
    {
        return WithPrefix(cursor, kind, DuePrefixCursor.Empty);
    }

    internal static DueSweepCursor DeferNext(StoreIdentity identity, DueSweepCursor? supplied)
    {
        var cursor = Match(identity, supplied);
        var kind = cursor.NextPrefix;
        return WithPrefix(cursor, kind, GetPrefix(cursor, kind));
    }

    private static bool Valid(DuePrefixCursor? cursor)
        => cursor is not null && (cursor.HasUpperBound
            ? cursor.UpperKey is { IsDefault: false, Length: > 0 and <= DueWorkProtocol.MaximumKeyBytes }
            : cursor.UpperKey is null && cursor.LastKey is null)
            && (cursor.LastKey is null || cursor.LastKey is
            { IsDefault: false, Length: > 0 and <= DueWorkProtocol.MaximumKeyBytes })
            && (cursor.LastKey is null || cursor.UpperKey is { } upper
                && cursor.LastKey.Value.AsSpan().SequenceCompareTo(upper.AsSpan()) <= 0);
}
