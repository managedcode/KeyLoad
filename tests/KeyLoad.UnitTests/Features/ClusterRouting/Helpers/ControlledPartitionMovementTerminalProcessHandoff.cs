using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Closes all parent owners, preserves original issued bytes and reopens fresh ownership after four children.</summary>
internal static class ControlledPartitionMovementTerminalProcessHandoff
{
    private const long EntryStep = 1;
    private const string RetainedRootKey = "KeyLoad.ControlledMovement.RetainedRoot";
    internal static async Task<OperationResult> ExecuteAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ControlledPartitionMovementLoopbackListeners listeners,
        ControlledPartitionMovementNode receiver, ReplicatedOperation original, CommitStage cut,
        CancellationToken token)
    {
        var clock = receiver.Database.EvaluationClock;
        var options = UnitExecutionOptions.NativeMovementProcess().Value;
        var input = new ControlledPartitionMovementProcessInput(receiver.PhysicalOwner, original,
            receiver.Store.Position, receiver.Journal.Log.State.LastIndex, cut, receiver.MaximumFixtureEntries);
        await ControlledPartitionMovementProcessFiles.WriteAsync(receiver.Root,
            ControlledPartitionMovementProcessProtocol.InputFile, input, token);
        var unaffected = ReferenceEquals(receiver, source) ? target : source;
        var unaffectedImage = ControlledPartitionMovementRawImage.Bytes(unaffected.Store);
        var unaffectedPosition = unaffected.Store.Position;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => JoinAsync(source, target, listeners), failures);
        OperationResult? recovered = null;
        if (failures.Count == 0)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await ControlledPartitionMovementTerminalProcessChildren.RunAsync(receiver.Root, options, clock, token);
                recovered = await ControlledPartitionMovementProcessFiles.ReadAsync<OperationResult>(receiver.Root,
                    ControlledPartitionMovementProcessProtocol.RecoveredFile, token);
                var verified = await ControlledPartitionMovementProcessFiles.ReadAsync<OperationResult>(receiver.Root,
                    ControlledPartitionMovementProcessProtocol.VerifiedFile, token);
                await Assert.That(NativeSerialization.Serialize(verified)
                    .SequenceEqual(NativeSerialization.Serialize(recovered))).IsTrue();
            }, failures);
        }
        var unsettled = failures.Any(ControlledPartitionMovementRetainedChild.ContainsUnsettled);
        if (!unsettled)
        { await ServerFailureObserver.ObserveAsync(() => OpenAsync(source, target, listeners), failures); }
        if (failures.Count != 0)
        {
            source.RetainRoot = true;
            target.RetainRoot = true;
            foreach (var error in failures)
            { error.Data[RetainedRootKey] = receiver.Root; }
        }
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(unaffected.Store.Position).IsEqualTo(unaffectedPosition);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(unaffected.Store)
            .SequenceEqual(unaffectedImage, StringComparer.Ordinal)).IsTrue();
        await Assert.That(receiver.Store.Position).IsEqualTo(checked(input.BeforePosition + EntryStep));
        await Assert.That(receiver.Journal.Log.State.LastIndex).IsEqualTo(checked(input.BeforeReplicaIndex + EntryStep));
        var replay = receiver.Journal.Submit(original, token);
        await Assert.That(NativeSerialization.Serialize(replay)
            .SequenceEqual(NativeSerialization.Serialize(recovered))).IsTrue();
        return recovered ?? throw new InvalidOperationException("The original process phase receipt is absent.");
    }

    private static Task JoinAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        ControlledPartitionMovementLoopbackListeners listeners)
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(source.JoinForChild, failures);
        ServerFailureObserver.Observe(target.JoinForChild, failures);
        ServerFailureObserver.Observe(listeners.JoinForChild, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return Task.CompletedTask;
    }

    private static Task OpenAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        ControlledPartitionMovementLoopbackListeners listeners)
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(source.OpenAfterChild, failures);
        ServerFailureObserver.Observe(target.OpenAfterChild, failures);
        ServerFailureObserver.Observe(listeners.OpenAfterChild, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return Task.CompletedTask;
    }
}
