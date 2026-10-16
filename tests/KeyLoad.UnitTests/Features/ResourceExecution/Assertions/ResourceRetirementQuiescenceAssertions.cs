using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementQuiescenceAssertions
{
    internal static async Task EqualAsync<T>(T expected, T actual)
    {
        await Assert.That(NativeSerialization.Serialize(expected).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(actual))).IsTrue();
        if (expected is OperationResult left && actual is OperationResult right)
        {
            await Assert.That(left.Error).IsEqualTo(right.Error);
            await Assert.That(left.SafeDetail).IsEqualTo(right.SafeDetail);
            await Assert.That(left.Json).IsEqualTo(right.Json);
            await Assert.That(left.NativeValue is null).IsEqualTo(right.NativeValue is null);
            if (left.NativeValue is not null)
            {
                await Assert.That(NativeSerialization.Serialize(left.NativeValue).AsSpan()
                    .SequenceEqual(NativeSerialization.Serialize(right.NativeValue))).IsTrue();
            }
        }
    }

    internal static async Task ModelsAsync(ResourceRetirementQuiescenceState state, CancellationToken token)
    {
        await ControlledPartitionMovementReceiptAssertions.ReplayAsync(state.Source.Journal.Submit(
            ControlledPartitionMovementCorpus.SeedOperation(state.Source.Database, state.RecordedAt), token), state.OriginalReceipt);
        await ControlledPartitionMovementModelAssertions.ReadAsync(state.Source, state.RecordedAt, state.Source.Store.Position, token);
        await ControlledPartitionMovementBlobAssertions.ReadAsync(state.Source, state.OriginalBlob, token);
        await state.OriginalAuthority.RemainsControlOwnedAsync(state.Source, state.Target);
    }

    internal static async Task ReplayAsync(ResourceRetirementQuiescenceState state, CancellationToken token)
    {
        var source = ControlledPartitionMovementExpiryOwnerState.Capture(state.Source);
        var target = ControlledPartitionMovementExpiryOwnerState.Capture(state.Target);
        foreach (var original in state.Originals)
        {
            await EqualAsync(original.Result, original.Owner.Journal.Submit(original.Original, token));
        }
        await source.AssertUnchangedAsync(state.Source);
        await target.AssertUnchangedAsync(state.Target);
    }
}
