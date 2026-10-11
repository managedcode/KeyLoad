using System.Security.Cryptography;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorFirstOpenStageResult
{
    private const int Version = 1;
    private const long FirstPosition = 1;
    private const string Invalid = "The original event vector first issuance result requires recovery.";

    internal static void Require(EventVectorFirstIssuanceResult result,
        EventVectorControlPhase actualStage, EventVectorFirstIssuanceState expectedState,
        ReadOnlyMemory<byte> firstBytes, string originalPrincipalId, Guid originalIncarnation,
        EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(actualStage);
        ArgumentNullException.ThrowIfNull(admission);
        var row = result.Issuance;
        var request = actualStage.OriginalRequest;
        if (row is null || result.Version != Version || row.Version != Version
            || row.PrincipalId != originalPrincipalId || row.State != expectedState || row.ControlPartition != request.ControlPartition
            || row.MapId != request.MapId || row.OriginalPublicCommandId != request.CommandId
            || row.OriginalParentRequestId != actualStage.OriginalParentRequestId
            || row.OriginalParentExpiresAt != actualStage.OriginalParentExpiresAt
            || !row.OriginalCapturedFirstOpenPhaseBytes.Span.SequenceEqual(firstBytes.Span)
            || !row.OriginalCoverageWitnessBytes.Span.SequenceEqual(actualStage.OriginalCoverageWitness.Span)
            || result.Receipt is null || result.Receipt.CommandId != result.NativeCommandId
            || result.Receipt.Token.Incarnation != originalIncarnation
            || !result.Receipt.Mutations.IsEmpty || result.Receipt.Token.Position < FirstPosition
            || result.Receipt.Token.AtomicPartitionId != request.ControlPartition.AtomicPartitionId)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        EventVectorFirstIssuanceValidation.RequireRequest(row, request, admission);
        var expectedId = EventVectorControlOperationIdentity.Create(actualStage, row.PrincipalId, admission).CommandId;
        if (expectedId != result.NativeCommandId || !CryptographicOperations.FixedTimeEquals(
            row.OriginalPublicRequestDigest.Span,
            EventVectorControlBodyProjection.RequestDigest(request, admission).Span))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
    }
}
