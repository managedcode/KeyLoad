namespace KeyLoad.Core;

internal static class EventVectorFirstCleanupResultValidation
{
    private const int Version = 1;
    private const long EmptyPosition = 0;
    private const string Invalid = "The original public first issuance cleanup result requires recovery.";

    internal static bool Handles(EventVectorControlAction action) => action is
        EventVectorControlAction.AbandonFirstIssuance or EventVectorControlAction.CleanupAbandonedIssuance;

    internal static EventFeedControlResult Read(EventVectorFirstIssuanceResult result,
        EventVectorControlPhase phase, string principalId, Guid incarnation, EventVectorAdmissionPolicy admission)
    {
        admission.RequireEncodedBytes(result.OriginalPublicResultBytes.Length);
        var row = result.Issuance;
        var released = phase.Action == EventVectorControlAction.CleanupAbandonedIssuance;
        if (!Handles(phase.Action) || result.Version != Version || result.NativeCommandId == Guid.Empty
            || row is null || row.PrincipalId != principalId || row.ControlPartition != phase.OriginalRequest.ControlPartition
            || row.MapId != phase.OriginalRequest.MapId
            || row.OriginalPublicCommandId != phase.OriginalFirstIssuancePublicCommandId
            || row.OriginalPublicCommandId != phase.OriginalRequest.OriginalFirstIssuancePublicCommandId
            || row.State != (released ? EventVectorFirstIssuanceState.Released : EventVectorFirstIssuanceState.Abandoned)
            || !row.OriginalCapturedFirstOpenPhaseBytes.Span.SequenceEqual(phase.OriginalCapturedFirstOpenPhaseBytes.Span)
            || !row.OriginalCoverageWitnessBytes.Span.SequenceEqual(phase.OriginalCoverageWitness.Span)
            || result.Receipt is null || result.Receipt.CommandId != result.NativeCommandId
            || result.Receipt.Token.Incarnation != incarnation
            || result.Receipt.Token.AtomicPartitionId != row.ControlPartition.AtomicPartitionId
            || result.Receipt.Token.Position <= EmptyPosition || result.Receipt.Mutations.IsDefault
            || result.OriginalPublicResultBytes.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
        var value = NativeSerialization.Deserialize<EventFeedControlResult>(result.OriginalPublicResultBytes.Span);
        if (value is null || value.Version != Version || value.PublicCommandId != phase.OriginalRequest.CommandId
            || value.MapId != row.MapId || value.Revision != EmptyPosition || value.CoverageGeneration != EmptyPosition
            || value.Released != released || value.Retained == released || value.Cursor is not null
            || value.OfferDigest is not null || value.Pages.IsDefault || !value.Pages.IsEmpty
            || value.Receipt is null || value.Receipt.CommandId != result.NativeCommandId
            || value.Receipt.Token != result.Receipt.Token || value.Receipt.Durability != result.Receipt.Durability
            || value.Receipt.Mutations.IsDefault || !value.Receipt.Mutations.SequenceEqual(result.Receipt.Mutations))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
        return value;
    }
}
