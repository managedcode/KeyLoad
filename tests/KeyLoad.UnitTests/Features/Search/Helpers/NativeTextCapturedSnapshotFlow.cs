using KeyLoad.Query.Features.Search;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextCapturedSnapshotFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        using var fixture = new TestDatabase(nativeReplicaAdmission: true);
        var seed = await NativeTextOnlineSeedFixture.CreateAsync(fixture, token);
        await using var runtime = new NativeTextOnlineTestRuntime(fixture);
        var request = new OnlineTextIndexMaintenanceRequest(Guid.NewGuid(), seed.Pin.Consumer,
            seed.Pin.Collection, seed.Pin.Field, seed.Pin.Generation, seed.Pin.NodeId, seed.Pin.Placement);
        var receipt = await NativeTextOnlineWholeFlow.RunAsync(fixture, runtime, request, token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = Task.Run(async () =>
        {
            using var observation = NativeTextPostingObservation.Enter(async current =>
            { entered.TrySetResult(); await release.Task.WaitAsync(current); });
            return await runtime.Search.SearchAsync(NativeTextMaintenanceTestValues.Principal,
                NativeTextBilingualAudit.Request(fixture.Partition, "ПРИВІТ"), cancellation.Token);
        }, token);
        var root = fixture.Directory + "-captured-snapshot-" + Guid.NewGuid().ToString("N");
        var failures = new List<Exception>();
        var ownedRoot = false;
        var observed = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await NativeTextCapturedPublicFlow.AwaitOriginalPostingAsync(entered, original, token);
            if (Directory.Exists(root))
            { throw new IOException("The fixture snapshot root already exists."); }
            Directory.CreateDirectory(root);
            ownedRoot = true;
            var before = fixture.Store.Identity;
            var path = Path.Combine(root, "snapshot.bin");
            var snapshot = fixture.Store.CreateSnapshot(path);
            var installed = fixture.Store.InstallSnapshot(path, snapshot.AppliedPosition);
            await Assert.That(installed).IsEqualTo(snapshot);
            await Assert.That(fixture.Store.Identity.NodeId).IsEqualTo(before.NodeId);
            await Assert.That(fixture.Store.Identity.Incarnation).IsEqualTo(before.Incarnation);
            await Assert.That(fixture.Store.Identity.ReadGeneration).IsGreaterThan(before.ReadGeneration);
            release.TrySetResult();
            var error = await Assert.ThrowsAsync<KeyLoadException>(async () => { _ = await original; });
            observed = true;
            await Assert.That((error ?? throw new InvalidOperationException()).Code).IsEqualTo(ErrorCode.TokenInvalidated);
            await NativeTextBilingualAudit.VerifyAsync(runtime.Search, fixture.Partition, token);
            var replay = await NativeTextOnlineWholeFlow.RunAsync(fixture, runtime, request, token);
            await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(receipt))).IsTrue();
            await NativeTextBilingualAudit.VerifyAsync(runtime.Search, fixture.Partition, token);
        }, failures);
        release.TrySetResult();
        if (!original.IsCompleted)
        { await ServerFailureObserver.ObserveAsync(cancellation.CancelAsync, failures); }
        if (!observed)
        { await ServerFailureObserver.ObserveAsync(async () => { _ = await original; }, failures); }
        if (ownedRoot)
        { ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        await runtime.DisposeAsync();
        await using var cold = new NativeTextOnlineTestRuntime(fixture);
        await NativeTextBilingualAudit.VerifyAsync(cold.Search, fixture.Partition, token);
        var coldReplay = await NativeTextOnlineWholeFlow.RunAsync(fixture, cold, request, token);
        await Assert.That(NativeSerialization.Serialize(coldReplay).SequenceEqual(NativeSerialization.Serialize(receipt))).IsTrue();
        await NativeTextBilingualAudit.VerifyAsync(cold.Search, fixture.Partition, token);
    }
}
