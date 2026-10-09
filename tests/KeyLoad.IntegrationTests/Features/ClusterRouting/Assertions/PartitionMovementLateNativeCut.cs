using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Full bounded canonical rows and native original outcomes at the actual cold owner cut.</summary>
internal sealed record PartitionMovementLateNativeCut(long Position, Dictionary<string, string> Rows,
    StoredOutcome? Outcome, PartitionMoveExpiredRetireCancellation? Cancellation,
    PartitionMoveParentHeader? Header, PartitionMoveParentPhase? OriginalPhase, PartitionMovePhaseGrant? CurrentGrant)
{
    private const long SingleFailedCommit = 1;
    private const long GenerationStep = 1;
    internal static PartitionMovementLateNativeCut Read(PartitionMovementLateNativeNode owner, ReplicatedOperation operation)
    {
        var phase = NativeCommandPayload.Read<PartitionMovePhaseCommand>(operation);
        var limits = owner.Application.Services.GetRequiredService<ServerRuntimeOptions>().Core.DatabaseLimits.Value;
        var database = owner.Partition.Database;
        return database.Store.Read(view =>
        {
            var page = view.Scan([], limits.MaxScanRecords);
            if (page.HasMore)
            { throw new InvalidOperationException("The complete native cut exceeded its bound."); }
            var selection = CommandOutcomeKeyResolver.Select(view, operation.PrincipalId, operation.Id,
                new(CommandOutcomeScopeKind.Partition, phase.Partition));
            var cancellation = PartitionMoveRetireCancellationStorage.Read(view, phase.Partition,
                phase.MoveId, operation.Id, limits.MaxBatchBytes);
            var header = PartitionMoveParentStorage.Header(view, phase.Partition, phase.MoveId, limits.MaxBatchBytes);
            var retained = PartitionMoveParentStorage.Phase(view, phase.Partition, phase.MoveId, operation.Id, limits.MaxBatchBytes);
            var grant = phase.ReceiverEffectAdmission is { } authority
                ? PartitionMoveGrantStorage.Read(view, phase.Partition, authority.OriginalGrant.GrantId, limits.MaxBatchBytes) : null;
            return new PartitionMovementLateNativeCut(database.Store.Position, page.Records.ToDictionary(row => Convert.ToHexString(row.Key.Span),
                row => Convert.ToHexString(row.Value.Span), StringComparer.Ordinal), selection.Outcome, cancellation, header, retained, grant);
        });
    }

    internal static async Task RequireFailureDeltaAsync(PartitionMovementLateNativeCut before,
        PartitionMovementLateNativeCut after, ReplicatedOperation operation, OperationResult result)
    {
        var phase = NativeCommandPayload.Read<PartitionMovePhaseCommand>(operation);
        await Assert.That(before.Outcome).IsNull();
        await RequireActualCancellationAsync(before, phase, operation);

        await Assert.That(after.Position).IsEqualTo(before.Position + SingleFailedCommit);
        await Assert.That(result.Error).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(result.SafeDetail).IsEqualTo(PartitionMoveProtocol.MissingAuthority);
        await Assert.That(after.Outcome).IsNotNull();
        await Assert.That(after.Outcome!.Result.Error).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(after.Outcome.Result.SafeDetail).IsEqualTo(PartitionMoveProtocol.MissingAuthority);
        await Assert.That(after.Outcome.Partition).IsEqualTo(phase.Partition);
        await Assert.That(after.Outcome.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Partition);
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            Convert.ToHexString(KeySpace.PartitionOutcome(phase.Partition, operation.PrincipalId, operation.Id)),
            Convert.ToHexString(CommandOutcomePartitionLocatorSerialization.ScopedKey(phase.Partition, operation.PrincipalId, operation.Id)),
            Convert.ToHexString(KeySpace.ClockBytes)
        };
        foreach (var key in before.Rows.Keys.Union(after.Rows.Keys, StringComparer.Ordinal))
        {
            if (!allowed.Contains(key))
            {
                await Assert.That(after.Rows.TryGetValue(key, out var actual)).IsTrue();
                await Assert.That(before.Rows.TryGetValue(key, out var expected)).IsTrue();
                await Assert.That(string.Equals(actual, expected, StringComparison.Ordinal)).IsTrue();
            }
        }
    }

    private static async Task RequireActualCancellationAsync(PartitionMovementLateNativeCut before,
        PartitionMovePhaseCommand phase, ReplicatedOperation original)
    {
        var admission = phase.ReceiverEffectAdmission ?? throw new InvalidOperationException("The actual original admission is absent.");
        await Assert.That(before.Cancellation).IsNotNull();
        await Assert.That(before.Cancellation!.OriginalPhaseCommandId).IsEqualTo(original.Id);
        await Assert.That(before.Cancellation.OriginalRequestNonce).IsEqualTo(admission.OriginalRequestNonce);
        await Assert.That(before.Cancellation.OriginalExpiresAt).IsEqualTo(admission.OriginalExpiresAt);
        await Assert.That(before.OriginalPhase).IsNotNull();
        await Assert.That(before.OriginalPhase!.OriginalResult).IsNull();
        await Assert.That(before.OriginalPhase.RetireCancellation).IsNotNull();
        await Assert.That(before.OriginalPhase.ObservationCheckpointReceipt).IsNotNull();
        await Assert.That(before.CurrentGrant).IsNotNull();
        await Assert.That(before.CurrentGrant!.Settlement).IsNull();
        await Assert.That(before.CurrentGrant.AbortDisposition).IsNull();
        await Assert.That(before.CurrentGrant.RetireCancellationDisposition).IsNotNull();
        var unchanged = before.CurrentGrant with { RetireCancellationDisposition = null };
        await Assert.That(NativeSerialization.Serialize(unchanged).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(admission.OriginalGrant))).IsTrue();
        await Assert.That(before.Header!.PendingOriginalPhaseCommandId).IsNotNull();
        await Assert.That(before.Header.CleanupGeneration).IsEqualTo(before.Cancellation.CleanupGeneration + GenerationStep);
    }
}
