using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.Core.Features.ClusterRouting.Identity;

/// <summary>Derives the frozen JSON fingerprint from the complete owning native phase without persisting a JSON duplicate.</summary>
internal static class PartitionMoveCanonicalCommandIdentity
{
    internal static void RequireBudget(PartitionMovePhaseCommand actual, int maximumBytes)
    {
        if (NativeSerialization.Measure(actual) > maximumBytes
            || NativeSerialization.Measure(Canonical(actual)) > maximumBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity);
        }
    }

    internal static string Fingerprint(Guid id, string principalId, PartitionMovePhaseCommand actual)
        => NativeOperationFingerprint.ComputeValue(id, OperationKind.PartitionMovementPhase, principalId, Canonical(actual));

    internal static string Fingerprint(Guid id, string principalId, PartitionMovePhaseCommand actual,
        int chunkCharacters)
        => NativeOperationFingerprint.ComputeValue(id, OperationKind.PartitionMovementPhase, principalId,
            Canonical(actual), chunkCharacters);

    internal static PartitionMovePhaseCommand Canonical(PartitionMovePhaseCommand actual)
    {
        var identity = actual with
        {
            ReceiverEffectAdmission = null
        };
        if (actual.Stage == PartitionMovePeerStage.RetireCancel)
        {
            var body = NativeSerialization.Deserialize<PartitionMoveRetireCancellationBody>(actual.Body.Span);
            identity = identity with
            {
                Body = NativeSerialization.Serialize(body with
                {
                    OriginalRequestBytes = ReadOnlyMemory<byte>.Empty,
                    OriginalRequestSignature = string.Empty,
                    CancellationExpiresAt = default,
                    OriginalSourceWitness = null
                })
            };
        }
        else if (actual.Stage == PartitionMovePeerStage.ControlAdmitCommand)
        {
            var body = NativeSerialization.Deserialize<PartitionControlAdmitBody>(actual.Body.Span);
            identity = identity with
            {
                Body = NativeSerialization.Serialize(body with
                {
                    OriginalOperation = body.OriginalOperation with
                    {
                        EvaluatedAt = default
                    },
                    ExpiresAt = default
                })
            };
        }
        return identity;
    }
}
