using KeyLoad.CrashHost.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed record D3PrivateRetirementParentImage(string[] Rows, long Position, long LastIndex, long CommittedIndex)
{
    internal static D3PrivateRetirementParentImage Capture(ControlledPartitionMovementNativeHost source)
        => new(ControlledPartitionMovementRawImage.Bytes(source.Store), source.Store.Position,
            source.Log.State.LastIndex, source.Log.State.CommittedIndex);

    internal async Task UnchangedAsync(ControlledPartitionMovementNativeHost source)
    {
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store).SequenceEqual(Rows, StringComparer.Ordinal)).IsTrue();
        await Assert.That(source.Store.Position).IsEqualTo(Position);
        await Assert.That(source.Log.State.LastIndex).IsEqualTo(LastIndex);
        await Assert.That(source.Log.State.CommittedIndex).IsEqualTo(CommittedIndex);
    }
}
