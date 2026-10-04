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
        return Clone(supplied);
    }

    internal static bool IsValid(DueSweepCursor cursor)
        => Enum.IsDefined(cursor.NextPrefix) && Valid(cursor.Schedules) && Valid(cursor.Sagas);

    internal static DueSweepCursor Clone(DueSweepCursor cursor)
        => cursor with { Schedules = Clone(cursor.Schedules), Sagas = Clone(cursor.Sagas) };

    internal static DueSweepCursor WithPrefix(DueSweepCursor cursor, DueWorkKind kind,
        DuePrefixCursor prefix)
        => kind == DueWorkKind.Schedule
            ? cursor with { Schedules = Clone(prefix), NextPrefix = DueWorkKind.Saga }
            : cursor with { Sagas = Clone(prefix), NextPrefix = DueWorkKind.Schedule };

    internal static DuePrefixCursor GetPrefix(DueSweepCursor cursor, DueWorkKind kind)
        => Clone(kind == DueWorkKind.Schedule ? cursor.Schedules : cursor.Sagas);

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
            ? cursor.UpperKey is { Length: > 0 and <= DueWorkProtocol.MaximumKeyBytes }
            : cursor.UpperKey is null && cursor.LastKey is null)
            && (cursor.LastKey is null || cursor.LastKey.Length is > 0 and <= DueWorkProtocol.MaximumKeyBytes)
            && (cursor.LastKey is null || cursor.UpperKey is not null
                && cursor.LastKey.AsSpan().SequenceCompareTo(cursor.UpperKey) <= 0);

    private static DuePrefixCursor Clone(DuePrefixCursor prefix)
        => prefix with { UpperKey = prefix.UpperKey?.ToArray(), LastKey = prefix.LastKey?.ToArray() };
}

internal sealed record DuePrefixCursor(bool HasUpperBound, byte[]? UpperKey, byte[]? LastKey)
{
    internal static DuePrefixCursor Empty { get; } = new(false, null, null);
}

internal sealed record DueSweepCursor(Guid Incarnation, long ReadGeneration, DueWorkKind NextPrefix,
    DuePrefixCursor Schedules, DuePrefixCursor Sagas)
{
    internal static DueSweepCursor Empty(Guid incarnation, long readGeneration)
        => new(incarnation, readGeneration, DueWorkKind.Schedule, DuePrefixCursor.Empty, DuePrefixCursor.Empty);
}
