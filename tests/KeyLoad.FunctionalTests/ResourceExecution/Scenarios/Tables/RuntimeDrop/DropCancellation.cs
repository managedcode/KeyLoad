using KeyLoad.Clustering;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.ResourceExecution;

internal static class DropCancellation
{
    internal static async Task ExecuteAsync(DropRuntimeProfile profile, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var hold = profile.Observer.Hold(id, GrainRequestPhase.BeforeSubmit);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
        var before = await DropRuntimeAssertions.CaptureAsync(profile, token).ConfigureAwait(false);
        var call = DropPublicSubmission.Start(profile, DropRuntimeProtocol.Request, id, caller.Token);
        try
        {
            await DropOperationFlow.WaitAsync(hold, call, token).ConfigureAwait(false);
            await Assert.That(call.Original.IsCompleted).IsFalse();
            await caller.CancelAsync().ConfigureAwait(false);
            await JoinCancelledAsync(profile, call).ConfigureAwait(false);
            await Assert.That(caller.IsCancellationRequested).IsTrue();
            await DropRuntimeAssertions.SameImageAsync(profile, before, token).ConfigureAwait(false);
            foreach (var node in profile.Hosts.Nodes)
            {
                var key = OutcomeStoreOracle.GlobalKey(PartitionStoreProtocol.AdministratorId, id);
                await Assert.That(node.Database.Store.Read(view => view.ReadOwnedValue(key))).IsNull();
            }
        }
        finally
        {
            hold.Released.TrySetResult();
        }
        await hold.Settled.Task.WaitAsync(token).ConfigureAwait(false);
        await hold.Disposed.Task.WaitAsync(token).ConfigureAwait(false);
        var retry = DropPublicSubmission.Start(profile, DropRuntimeProtocol.Request, id, token);
        var result = DropRuntimeAssertions.Value<DropTableResult>(await retry.Original.ConfigureAwait(false));
        await DropRuntimeAssertions.DroppedAsync(profile, id, DropRuntimeProtocol.Request, result, token).ConfigureAwait(false);
        var original = await DropRuntimeAssertions.StoredDropAsync(profile, id,
            DropRuntimeProtocol.Request, null, token).ConfigureAwait(false);
        await DropOperationFlow.ColdReplayAsync(profile, id, result, original, token).ConfigureAwait(false);
    }

    private static async Task JoinCancelledAsync(DropRuntimeProfile profile, DropPublicCall call)
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
