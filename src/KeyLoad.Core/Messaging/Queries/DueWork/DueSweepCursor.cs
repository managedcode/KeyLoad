using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Core;

internal sealed record DueSweepCursor(Guid Incarnation, long ReadGeneration, DueWorkKind NextPrefix,
    DuePrefixCursor Schedules, DuePrefixCursor Sagas)
{
    internal static DueSweepCursor Empty(Guid incarnation, long readGeneration)
        => new(incarnation, readGeneration, DueWorkKind.Schedule, DuePrefixCursor.Empty, DuePrefixCursor.Empty);
}
