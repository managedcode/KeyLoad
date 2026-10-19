using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Core;

/// <summary>Resumes due discovery only within the actual physical read generation.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(DueSweepCursor.SerializerAlias)]
public sealed record DueSweepCursor([property: Orleans.Id(0)] Guid Incarnation,
    [property: Orleans.Id(1)] long ReadGeneration,
    [property: Orleans.Id(2)] DueWorkKind NextPrefix,
    [property: Orleans.Id(3)] DuePrefixCursor Schedules,
    [property: Orleans.Id(4)] DuePrefixCursor Sagas)
{
    internal const string SerializerAlias = "keyload.core.due-sweep-cursor";

    internal static DueSweepCursor Empty(Guid incarnation, long readGeneration)
        => new(incarnation, readGeneration, DueWorkKind.Schedule, DuePrefixCursor.Empty, DuePrefixCursor.Empty);
}
