using System.Collections.Immutable;

namespace KeyLoad.Core.Features.Messaging;

internal sealed record DueRejectedRecord(DueWorkKind Kind, ErrorCode Code);

internal sealed record DueWorkPage(
    ImmutableArray<DueWorkHint> Jobs,
    ImmutableArray<DueRejectedRecord> Rejected,
    DueSweepCursor Cursor,
    DueWorkKind ScannedPrefix,
    bool PrefixCompleted,
    int ExaminedRecords,
    long ExaminedBytes,
    long AdmittedValueBytes);
