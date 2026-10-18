using KeyLoad.Clustering;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class NativeDropCancellation
{
    internal static async Task ExecuteAsync(NativeDropRuntimeProfile profile, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var hold = profile.Observer.Hold(id, GrainRequestPhase.BeforeSubmit);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
        var call = NativeDropPublicSubmission.Start(profile, NativeDropRuntimeProtocol.Request, id, caller.Token);
        var before = await NativeDropRuntimeAssertions.CaptureAsync(profile, token).ConfigureAwait(false);
        try
        {
            await NativeDropOperationFlow.WaitAsync(hold, call, token).ConfigureAwait(false);
            await Assert.That(call.Original.IsCompleted).IsFalse();
            await caller.CancelAsync().ConfigureAwait(false);
            await JoinCancelledAsync(profile, call).ConfigureAwait(false);
            await hold.Disposed.Task.WaitAsync(token).ConfigureAwait(false);
            await Assert.That(hold.SelectedCancellation.IsCancellationRequested).IsTrue();
        }
        finally
        {
            hold.Released.TrySetResult();
        }
        await NativeDropRuntimeAssertions.SameImageAsync(profile, before, token).ConfigureAwait(false);
        foreach (var node in profile.Hosts.Nodes)
        {
            var key = OutcomeStoreOracle.GlobalKey(PartitionStoreProtocol.AdministratorId, id);
            await Assert.That(node.Database.Store.Read(view => view.ReadOwnedValue(key))).IsNull();
        }
        var retry = NativeDropPublicSubmission.Start(profile, NativeDropRuntimeProtocol.Request, id, token);
        var result = NativeDropRuntimeAssertions.Value<DropTableResult>(await retry.Original.ConfigureAwait(false));
        await NativeDropRuntimeAssertions.DroppedAsync(profile, id, NativeDropRuntimeProtocol.Request, result, token).ConfigureAwait(false);
        var original = await NativeDropRuntimeAssertions.StoredDropAsync(profile, id,
            NativeDropRuntimeProtocol.Request, null, token).ConfigureAwait(false);
        await NativeDropOperationFlow.ColdReplayAsync(profile, id, result, original, token).ConfigureAwait(false);
    }

    private static async Task JoinCancelledAsync(NativeDropRuntimeProfile profile, NativeDropPublicCall call)
    {
        try
        {
            var reply = await call.Original.ConfigureAwait(false);
            await Assert.That(reply.Error is ErrorCode.Cancelled or ErrorCode.UnknownWriteOutcome).IsTrue();
        }
        catch (OperationCanceledException original)
        {
            profile.ExpectFailure(call.Original, original);
        }
    }
}
