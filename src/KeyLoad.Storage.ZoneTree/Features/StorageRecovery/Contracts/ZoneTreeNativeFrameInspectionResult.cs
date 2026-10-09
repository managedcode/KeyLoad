namespace KeyLoad.Storage.ZoneTree;

/// <summary>Internal stopped-owner native evidence; outcome bytes never enter diagnostic or public transport payloads.</summary>
internal sealed record ZoneTreeNativeFrameInspectionResult(Guid NodeId, Guid Incarnation, int FormatVersion,
    long Sequence, int PayloadBytes, string PayloadSha256, int MutationCount, long RawMutationBytes,
    long ExaminedFrames, long ExaminedBytes, int MaximumObservedPayloadBytes, ReadOnlyMemory<byte> OutcomeBytes)
{
    public override string ToString() => nameof(ZoneTreeNativeFrameInspectionResult);
}
