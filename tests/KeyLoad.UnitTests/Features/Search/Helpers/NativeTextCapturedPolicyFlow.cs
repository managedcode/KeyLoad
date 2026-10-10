using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextCapturedPolicyFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        using var fixture = new TestDatabase(nativeReplicaAdmission: true);
        var seed = await NativeTextOnlineSeedFixture.CreateAsync(fixture, token);
        await using var runtime = new NativeTextOnlineTestRuntime(fixture);
        var request = new OnlineTextIndexMaintenanceRequest(Guid.NewGuid(), seed.Pin.Consumer,
            seed.Pin.Collection, seed.Pin.Field, seed.Pin.Generation, seed.Pin.NodeId, seed.Pin.Placement);
        _ = await NativeTextOnlineWholeFlow.RunAsync(fixture, runtime, request, token);
        var originalBytes = StoredBytes(fixture, request);
        var principal = fixture.Store.Read(view => fixture.Database.Principal(view,
            NativeTextMaintenanceTestValues.Principal, fixture.Database.EvaluationClock.GetUtcNow()));
        var repair = principal with { Id = "online-text-reader-policy-repair" };
        _ = fixture.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(repair)).Get<PrincipalRecord>();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = Task.Run(async () =>
        {
            using var observation = NativeTextPostingObservation.Enter(async current =>
            { entered.TrySetResult(); await release.Task.WaitAsync(current); });
            return await runtime.Search.SearchAsync(principal.Id,
                NativeTextBilingualAudit.Request(fixture.Partition, "ПРИВІТ"), cancellation.Token);
        }, token);
        var failures = new List<Exception>();
        var observed = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await NativeTextCapturedPublicFlow.AwaitOriginalPostingAsync(entered, original, token);
            _ = await NativeTextCapturedPublicContinuation.SwapAsync(fixture, runtime, request, token);
            var revoked = principal with { Revoked = true, PolicyEpoch = checked(principal.PolicyEpoch + 1) };
            _ = fixture.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(revoked), repair.Id).Get<PrincipalRecord>();
            release.TrySetResult();
            var error = await Assert.ThrowsAsync<KeyLoadException>(async () => { _ = await original; });
            observed = true;
            await Assert.That((error ?? throw new InvalidOperationException()).Code).IsEqualTo(ErrorCode.Unauthenticated);
            _ = fixture.Submit(OperationKind.ConfigurePrincipal,
                new ConfigurePrincipalRequest(principal with { PolicyEpoch = checked(principal.PolicyEpoch + 2) }), repair.Id).Get<PrincipalRecord>();
            var oldReplay = await Assert.ThrowsAsync<KeyLoadException>(() => NativeTextOnlineWholeFlow.RunAsync(fixture, runtime, request, token));
            await Assert.That((oldReplay ?? throw new InvalidOperationException()).Code).IsEqualTo(ErrorCode.PermissionDenied);
            await Assert.That(StoredBytes(fixture, request).SequenceEqual(originalBytes)).IsTrue();
            _ = await NativeTextOnlineWholeFlow.RunAsync(fixture, runtime, request with { CommandId = Guid.NewGuid() }, token);
            await NativeTextCapturedPublicContinuation.HealthyAsync(runtime, fixture.Partition, token);
        }, failures);
        release.TrySetResult();
        if (!original.IsCompleted)
        { await ServerFailureObserver.ObserveAsync(cancellation.CancelAsync, failures); }
        if (!observed)
        { await ServerFailureObserver.ObserveAsync(async () => { _ = await original; }, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        await runtime.DisposeAsync();
        await using var cold = new NativeTextOnlineTestRuntime(fixture);
        await NativeTextCapturedPublicContinuation.HealthyAsync(cold, fixture.Partition, token);
        await Assert.That(StoredBytes(fixture, request).SequenceEqual(originalBytes)).IsTrue();
    }

    private static byte[] StoredBytes(TestDatabase fixture, OnlineTextIndexMaintenanceRequest request)
        => fixture.Store.Read(view => NativeSerialization.Serialize(CommandOutcomeKeyResolver.Select(view,
            NativeTextMaintenanceTestValues.Principal, request.CommandId,
            new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, request.Consumer.Partition)).Outcome
                ?? throw new InvalidOperationException("The original native publication outcome is absent.")));
}
