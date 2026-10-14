using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterRouting;
namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal sealed record SqlTypedInsertRetiredImage(string[] Source, string[] Target, long SourceCut,
    long TargetCut, long SourceIndex, long TargetIndex)
{
    internal static SqlTypedInsertRetiredImage Capture(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target)
        => new(ControlledPartitionMovementRawImage.Bytes(source.Store), ControlledPartitionMovementRawImage.Bytes(target.Store),
            source.Store.Position, target.Store.Position, source.Journal.Log.State.LastIndex, target.Journal.Log.State.LastIndex);
    internal static async Task ObserveAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        Func<Task> operation, params Func<Task>[] conservation)
    {
        var image = Capture(source, target);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        foreach (var check in conservation)
        { await ServerFailureObserver.ObserveAsync(check, failures); }
        await ServerFailureObserver.ObserveAsync(() => image.UnchangedAsync(source, target), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal async Task UnchangedAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(source.Store.Position).IsEqualTo(SourceCut), failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(target.Store.Position).IsEqualTo(TargetCut), failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(SourceIndex), failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(target.Journal.Log.State.LastIndex).IsEqualTo(TargetIndex), failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store).SequenceEqual(Source, StringComparer.Ordinal)).IsTrue(), failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(ControlledPartitionMovementRawImage.Bytes(target.Store).SequenceEqual(Target, StringComparer.Ordinal)).IsTrue(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
