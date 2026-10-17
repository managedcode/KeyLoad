using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class ControlledPartitionMovementRetainedOutcomeAudit
{
    private ControlledPartitionMovementRetainedOutcome[] records = [];

    internal async Task CaptureAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target, CancellationToken cancellationToken)
    {
        records = await ControlledPartitionMovementOutcomeInventory.CaptureAsync(source);
        await CheckAsync(source, target);
        await ReplayAsync(source, target, cancellationToken, includeBlob: true);
    }

    internal async Task CheckAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target)
        => await ControlledPartitionMovementRetainedOutcomeAssertions.RequireAsync(source, target, records);

    internal async Task ReplayAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        CancellationToken cancellationToken, bool includeBlob = false)
    {
        var sourceImage = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var targetImage = ControlledPartitionMovementRawImage.Bytes(target.Store);
        var position = source.Store.Position;
        var targetPosition = target.Store.Position;
        var targetIndex = target.Journal.Log.State.LastIndex;
        var index = source.Journal.Log.State.LastIndex;
        foreach (var record in records.Where(record => includeBlob || record.Scope == CommandOutcomeScopeKind.Global
            || record.Index == ControlledPartitionMovementOutcomeInventory.BatchIndex))
        {
            var replay = source.Journal.Submit(record.Operation, cancellationToken);
            await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(record.Outcome.Result))).IsTrue();
        }
        await ControlledPartitionMovementOutcomeContinuation.ConflictAsync(source, records.First().Operation);
        await Assert.That(source.Store.Position).IsEqualTo(position);
        await Assert.That(target.Store.Position).IsEqualTo(targetPosition);
        await Assert.That(target.Journal.Log.State.LastIndex).IsEqualTo(targetIndex);
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(index);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store).SequenceEqual(sourceImage, StringComparer.Ordinal)).IsTrue();
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(target.Store).SequenceEqual(targetImage, StringComparer.Ordinal)).IsTrue();
        await CheckAsync(source, target);
    }

    internal async Task FinishAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        CancellationToken cancellationToken)
    {
        await ReplayAsync(source, target, cancellationToken);
        await ControlledPartitionMovementOutcomeContinuation.HealthyColdAsync(source, target, cancellationToken);
        await ReplayAsync(source, target, cancellationToken);
    }
}
