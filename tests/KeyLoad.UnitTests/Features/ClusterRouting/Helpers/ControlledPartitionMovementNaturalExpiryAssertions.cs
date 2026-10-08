using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Observes real owner-clock expiry of a genuinely applied, still-unsettled original retirement grant.</summary>
internal static class ControlledPartitionMovementNaturalExpiryAssertions
{
    internal static async Task AssertAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        ServerRuntimeOptions runtime, PartitionMovementPeerAdmission admission, PartitionMovementTransportRequest original,
        PartitionMovePhaseGrant issued, OperationResult actualOriginal, CancellationToken cancellationToken)
    {
        await Assert.That(issued.GrantId).IsEqualTo(original.Envelope.Grant!.GrantId);
        await Assert.That(issued.ExpiresAt).IsEqualTo(original.Envelope.ExpiresAt);
        await Assert.That(NativeSerialization.Serialize(issued)
            .SequenceEqual(NativeSerialization.Serialize(original.Envelope.Grant))).IsTrue();
        await WaitForOriginalExpiryAsync(source.Database.EvaluationClock, issued.ExpiresAt, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await Assert.That(source.Database.EvaluationClock.GetUtcNow() >= issued.ExpiresAt).IsTrue();
        await RequireOriginalGrantAsync(source, issued);
        var sourceBefore = ControlledPartitionMovementExpiryOwnerState.Capture(source);
        var targetBefore = ControlledPartitionMovementExpiryOwnerState.Capture(target);
        var denied = await Assert.ThrowsAsync<KeyLoadException>(() =>
            ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime, admission, original, cancellationToken));
        await Assert.That(denied).IsNotNull();
        var failure = denied ?? throw new InvalidOperationException("The actually expired original issued grant was not denied.");
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await ObserveOriginalAsync(source, runtime, original, actualOriginal, cancellationToken);
        await RequireOriginalGrantAsync(source, issued);
        await sourceBefore.AssertUnchangedAsync(source);
        await targetBefore.AssertUnchangedAsync(target);
    }

    private static async Task WaitForOriginalExpiryAsync(TimeProvider clock, DateTimeOffset originalExpiry,
        CancellationToken cancellationToken)
    {
        while (clock.GetUtcNow() < originalExpiry)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = originalExpiry - clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            { break; }
            var delay = TimeSpan.FromMilliseconds(Math.Ceiling(remaining.TotalMilliseconds));
            await Task.Delay(delay, clock, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task RequireOriginalGrantAsync(ControlledPartitionMovementNode source, PartitionMovePhaseGrant issued)
    {
        var actual = source.Store.Read(view => PartitionMoveGrantStorage.Read(view, issued.Partition,
            issued.GrantId, source.Database.Limits.MaxBatchBytes));
        await Assert.That(actual).IsNotNull();
        await Assert.That(actual!.Settlement).IsNull();
        await Assert.That(actual.AbortDisposition).IsNull();
        await Assert.That(NativeSerialization.Serialize(actual).SequenceEqual(NativeSerialization.Serialize(issued))).IsTrue();
    }

    private static async Task ObserveOriginalAsync(ControlledPartitionMovementNode source, ServerRuntimeOptions runtime,
        PartitionMovementTransportRequest original, OperationResult actualOriginal, CancellationToken cancellationToken)
    {
        var work = new ReadExecutionBudget(runtime.Core.DatabaseLimits, source.Database.EvaluationClock, cancellationToken);
        var grant = work.CreateReadGrant(source.Database.Limits.MaxQueryReadBytes, source.Database.Limits.MaxScanRecords);
        var actual = source.Database.ResolveVerifiedPartitionMovementOutcome(PhysicalShardCatalogFixture.RootPrincipalId,
            original.Envelope, original.CommandId, work, grant);
        await Assert.That(actual.Error).IsNull();
        await Assert.That(NativeSerialization.Serialize(actual).SequenceEqual(NativeSerialization.Serialize(actualOriginal))).IsTrue();
    }
}
