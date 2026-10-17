using System.Text;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.UnitTests.Features.InternalSerialization;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Compares actual issued native operations with the complete independent former JSON identity oracle.</summary>
internal static class PartitionMovementNativeIdentityAssertions
{
    private const int SmallFingerprintChunk = 2;
    private const int AlternateFingerprintChunk = 3;
    private const string WitnessKind = "PartitionMovementNativeOriginalIdentity";

    internal static async Task VerifyAsync(ReplicatedOperation operation, DatabaseLimits limits)
    {
        var root = PartitionMovementNativePayload.ReadAuthenticated(operation);
        var canonical = Canonical(root.Original);
        var oldJson = JsonSerializer.Serialize(canonical, JsonDefaults.Options);
        var expected = JsonData.Fingerprint(new
        {
            operation.Id,
            operation.Kind,
            operation.PrincipalId,
            PayloadJson = oldJson
        });
        await Assert.That(operation.PayloadJson).IsEmpty();
        await Assert.That(operation.NativePayload.Length).IsLessThanOrEqualTo(limits.MaxBatchBytes);
        await Assert.That(NativeOperationFingerprint.Compute(operation)).IsEqualTo(expected);
        var payload = NativeAuthorityFixture.Read(operation);
        var claims = NativeSerialization.Deserialize<NativeCommandAuthority>(payload.Authority.Span);
        await Assert.That(claims.Fingerprint).IsEqualTo(expected);
        await Assert.That(NativeOperationFingerprint.Compute(operation.Id, operation.Kind, operation.PrincipalId,
            ReadOnlySpan<byte>.Empty, payload.Value)).IsEqualTo(expected);
        var chunk = SerializationExecutionRegistration.Process.Value.FingerprintChunkCharacters == SmallFingerprintChunk
            ? AlternateFingerprintChunk : SmallFingerprintChunk;
        var configured = Options.Create(new SerializationExecutionOptions
        {
            FingerprintChunkCharacters = chunk
        });
        await Assert.That(NativeOperationFingerprint.Compute(operation, configured)).IsEqualTo(expected);
        await Assert.That(NativeOperationFingerprint.Compute(operation.Id, operation.Kind, operation.PrincipalId,
            ReadOnlySpan<byte>.Empty, payload.Value, configured)).IsEqualTo(expected);
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new
        {
            Kind = WitnessKind,
            Stage = root.Original.Stage,
            NativeBytes = operation.NativePayload.Length,
            FormerJsonBytes = Encoding.UTF8.GetByteCount(oldJson),
            MaximumBytes = limits.MaxBatchBytes,
            FullNativeRootRetained = true,
            FormerFingerprintEqual = true,
            NonDefaultFingerprintChunk = chunk,
            ConfiguredLocalAndBorrowedFingerprintEqual = true
        }));
    }

    private static PartitionMovePhaseCommand Canonical(PartitionMovePhaseCommand phase)
    {
        phase = phase with
        {
            ReceiverEffectAdmission = null
        };
        if (phase.Stage == PartitionMovePeerStage.ControlAdmitCommand)
        {
            var body = NativeSerialization.Deserialize<PartitionControlAdmitBody>(phase.Body.Span);
            return phase with
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
        if (phase.Stage == PartitionMovePeerStage.RetireCancel)
        {
            var body = NativeSerialization.Deserialize<PartitionMoveRetireCancellationBody>(phase.Body.Span);
            return phase with
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
        return phase;
    }
}
