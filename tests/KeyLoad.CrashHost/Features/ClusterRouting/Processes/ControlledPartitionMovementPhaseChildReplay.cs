namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Requires exact original native replay and one original physical effect without another journal entry.</summary>
internal static class ControlledPartitionMovementPhaseChildReplay
{
    private const long EntryStep = 1;

    internal static Task AssertAsync(ControlledPartitionMovementNativeNode actual,
        ControlledPartitionMovementProcessInput input, OperationResult original, CancellationToken cancellationToken)
    {
        if (original.Error is not null || original.SafeDetail is not null
            || actual.Store.Position != checked(input.BeforePosition + EntryStep)
            || actual.Journal.Log.State.LastIndex != checked(input.BeforeReplicaIndex + EntryStep))
        { throw new InvalidOperationException(ControlledPartitionMovementProcessProtocol.Invalid); }
        var bytes = NativeSerialization.Serialize(original);
        var image = ControlledPartitionMovementProcessRawImage.Bytes(actual.Store);
        var position = actual.Store.Position;
        var index = actual.Journal.Log.State.LastIndex;
        var replay = actual.Journal.Submit(input.OriginalOperation, cancellationToken);
        if (!NativeSerialization.Serialize(replay).AsSpan().SequenceEqual(bytes)
            || actual.Store.Position != position || actual.Journal.Log.State.LastIndex != index
            || !ControlledPartitionMovementProcessRawImage.Bytes(actual.Store)
                .SequenceEqual(image, StringComparer.Ordinal))
        { throw new InvalidOperationException(ControlledPartitionMovementProcessProtocol.Invalid); }
        return Task.CompletedTask;
    }
}
