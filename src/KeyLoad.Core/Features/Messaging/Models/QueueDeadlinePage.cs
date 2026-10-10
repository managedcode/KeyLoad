using System.Collections.Immutable;

namespace KeyLoad.Core.Features.Messaging;

internal sealed record QueueDeadlinePage(ImmutableArray<QueueDeadlineHint> Jobs,
    ImmutableArray<ErrorCode> Rejected, QueueDeadlineCursor Cursor, int ExaminedRecords, long ExaminedBytes);
