using System.Diagnostics;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

/// <summary>Reads disposable due-work hints from the canonical schedule and saga prefixes.</summary>
internal static class DueWorkDiscovery
{
    private static readonly byte[] SchedulePrefix = KeyCodec.Encode(DueWorkProtocol.ScheduleSpace);
    private static readonly byte[] SagaPrefix = KeyCodec.Encode(DueWorkProtocol.SagaSpace);

    internal static DueWorkPage ReadPage(DatabaseEngine database, DueSweepCursor? supplied,
        DateTimeOffset wakeAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        cancellationToken.ThrowIfCancellationRequested();
        if (wakeAt.Offset != TimeSpan.Zero)
        {
            throw Errors.Fail(ErrorCode.Validation, DueWorkProtocol.InvalidWakeInstant);
        }

        var state = new DuePageState(database.Limits, wakeAt, Stopwatch.GetTimestamp(), cancellationToken);
        var page = database.Store.Read(view =>
        {
            var cursor = DueWorkCursor.Match(database.Store.Identity, supplied);
            return DuePrefixScanner.Read(database, view, cursor, state, Prefix(cursor.NextPrefix));
        });
        state.Check();
        return page with { ExaminedBytes = state.ExaminedBytes };
    }

    internal static DueWorkPage EmptyPage(DueSweepCursor? supplied, StoreIdentity identity)
    {
        var matched = DueWorkCursor.Match(identity, supplied);
        return new([], [], matched, matched.NextPrefix, false, 0, 0, 0);
    }

    private static byte[] Prefix(DueWorkKind kind) => kind == DueWorkKind.Schedule ? SchedulePrefix : SagaPrefix;
}
