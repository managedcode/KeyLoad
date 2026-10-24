using System.Collections.Immutable;

namespace KeyLoad.Core.Messaging;

/// <summary>Reports one real bounded read of the persisted schedule or saga prefix.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(DueWorkPage.SerializerAlias)]
public sealed record DueWorkPage([property: Orleans.Id(0)] ImmutableArray<DueWorkHint> Jobs,
    [property: Orleans.Id(1)] ImmutableArray<DueRejectedRecord> Rejected,
    [property: Orleans.Id(2)] DueSweepCursor Cursor,
    [property: Orleans.Id(3)] DueWorkKind ScannedPrefix,
    [property: Orleans.Id(4)] bool PrefixCompleted,
    [property: Orleans.Id(5)] int ExaminedRecords,
    [property: Orleans.Id(6)] long ExaminedBytes,
    [property: Orleans.Id(7)] long AdmittedValueBytes)
{
    internal const string SerializerAlias = "keyload.core.due-work-page";
}
