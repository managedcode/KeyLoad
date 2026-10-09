namespace KeyLoad.Server.Features.ClusterRouting;

internal static class MovementFrameObservationFields
{
    internal const long AbsentIndex = 0;
    internal const long AbsentTerm = 0;
    internal const int AbsentCap = 0;
    private static readonly string[] OwnerFields =
        [nameof(MovementFrameObservationOwner.Version), nameof(MovementFrameObservationOwner.Kind), nameof(MovementFrameObservationOwner.SessionId), nameof(MovementFrameObservationOwner.Voter)];
    internal static ReadOnlySpan<string> Owner => OwnerFields;
    private static readonly string[] SelectionFields =
        [nameof(MovementFrameObservationSelection.Version), nameof(MovementFrameObservationSelection.Kind), nameof(MovementFrameObservationSelection.SessionId), nameof(MovementFrameObservationSelection.SelectionId), nameof(MovementFrameObservationSelection.MoveId), nameof(MovementFrameObservationSelection.Partition), nameof(MovementFrameObservationSelection.OperatorPrincipalId), nameof(MovementFrameObservationSelection.PhysicalShardId), nameof(MovementFrameObservationSelection.Incarnation)];
    internal static ReadOnlySpan<string> Selection => SelectionFields;
    private static readonly string[] ObservationFields =
        [nameof(MovementFrameObservationRecord.Version), nameof(MovementFrameObservationRecord.Kind), nameof(MovementFrameObservationRecord.SessionId), nameof(MovementFrameObservationRecord.SelectionId), nameof(MovementFrameObservationRecord.MoveId), nameof(MovementFrameObservationRecord.Partition), nameof(MovementFrameObservationRecord.OperatorPrincipalId), nameof(MovementFrameObservationRecord.ReceiverPrincipalId), nameof(MovementFrameObservationRecord.PhysicalShardId), nameof(MovementFrameObservationRecord.Incarnation), nameof(MovementFrameObservationRecord.Voter), nameof(MovementFrameObservationRecord.EffectCommandId), nameof(MovementFrameObservationRecord.OriginalRequestNonce), nameof(MovementFrameObservationRecord.OriginalExpiresAt), nameof(MovementFrameObservationRecord.EntryIndex), nameof(MovementFrameObservationRecord.EntryTerm), nameof(MovementFrameObservationRecord.RejectedPrefixBytes), nameof(MovementFrameObservationRecord.MaximumFrameBytes)];
    internal static ReadOnlySpan<string> Observation => ObservationFields;
}
