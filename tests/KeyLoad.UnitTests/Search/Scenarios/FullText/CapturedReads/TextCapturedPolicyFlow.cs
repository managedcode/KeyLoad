using KeyLoad.Core.Features.CommandOutcomes.Contracts;
using KeyLoad.Core.Features.CommandOutcomes.Execution;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Search;

internal static class TextCapturedPolicyFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        using var fixture = new TestDatabase();
        var seed = await TextOnlineSeedFixture.CreateAsync(fixture, token);
        await using var runtime = new TextOnlineTestRuntime(fixture);
        var request = new OnlineTextIndexMaintenanceRequest(Guid.NewGuid(), seed.Pin.Consumer,
            seed.Pin.Collection, seed.Pin.Field, seed.Pin.Generation, seed.Pin.NodeId, seed.Pin.Placement);
        _ = await TextOnlineWholeFlow.RunAsync(fixture, runtime, request, token);
        var originalBytes = StoredBytes(fixture, request);
        var (principal, repair) = ConfigureRepairPrincipal(fixture);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = Task.Run(async () =>
        {
            using var observation = TextPostingObservation.Enter(async current =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(current);
            });
            return await runtime.Search.SearchAsync(principal.Id,
                TextBilingualAudit.Request(fixture.Partition, "ПРИВІТ"), cancellation.Token);
        }, token);
        var failures = new List<Exception>();
        var observed = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await TextCapturedPublicFlow.AwaitOriginalPostingAsync(entered, original, token);
            _ = await TextCapturedPublicContinuation.SwapAsync(fixture, runtime, request, token);
            var revoked = principal with
            {
                Revoked = true,
                PolicyEpoch = checked(principal.PolicyEpoch + 1)
            };
            _ = fixture.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(revoked), repair.Id).Get<PrincipalRecord>();
            release.TrySetResult();
            var error = await Assert.ThrowsAsync<KeyLoadException>(async () => { _ = await original; });
            observed = true;
            await Assert.That((error ?? throw new InvalidOperationException()).Code).IsEqualTo(ErrorCode.Unauthenticated);
            _ = fixture.Submit(OperationKind.ConfigurePrincipal,
                new ConfigurePrincipalRequest(principal with
                {
                    PolicyEpoch = checked(principal.PolicyEpoch + 2)
                }), repair.Id).Get<PrincipalRecord>();
            var oldReplay = await Assert.ThrowsAsync<KeyLoadException>(() => TextOnlineWholeFlow.RunAsync(fixture, runtime, request, token));
            await Assert.That((oldReplay ?? throw new InvalidOperationException()).Code).IsEqualTo(ErrorCode.PermissionDenied);
            await Assert.That(StoredBytes(fixture, request).SequenceEqual(originalBytes)).IsTrue();
            _ = await TextOnlineWholeFlow.RunAsync(fixture, runtime, request with
            {
                CommandId = Guid.NewGuid()
            }, token);
            await TextCapturedPublicContinuation.HealthyAsync(runtime, fixture.Partition, token);
        }, failures);
        await SettleOriginalAsync(original, cancellation, release, observed, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await runtime.DisposeAsync();
        await using var cold = new TextOnlineTestRuntime(fixture);
        await TextCapturedPublicContinuation.HealthyAsync(cold, fixture.Partition, token);
        await Assert.That(StoredBytes(fixture, request).SequenceEqual(originalBytes)).IsTrue();
    }

    private static byte[] StoredBytes(TestDatabase fixture, OnlineTextIndexMaintenanceRequest request)
        => fixture.Store.Read(view => OrleansSerialization.Serialize(CommandOutcomeKeyResolver.Select(view,
            TextMaintenanceTestValues.Principal, request.CommandId,
            new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, request.Consumer.Partition)).Outcome
                ?? throw new InvalidOperationException("The original execution publication outcome is absent.")));

    private static async Task SettleOriginalAsync(Task<RankedDocument[]> original, CancellationTokenSource cancellation,
        TaskCompletionSource release, bool observed, List<Exception> failures)
    {
        release.TrySetResult();
        if (!original.IsCompleted)
        {
            await ServerFailureObserver.ObserveAsync(cancellation.CancelAsync, failures);
        }
        if (!observed)
        {
            await ServerFailureObserver.ObserveAsync(async () => { _ = await original; }, failures);
        }
    }

    private static (PrincipalRecord Principal, PrincipalRecord Repair) ConfigureRepairPrincipal(TestDatabase fixture)
    {
        var principal = fixture.Store.Read(view => fixture.Database.Principal(view,
            TextMaintenanceTestValues.Principal, fixture.Database.EvaluationClock.GetUtcNow()));
        var repair = principal with
        {
            Id = "online-text-reader-policy-repair"
        };
        _ = fixture.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(repair)).Get<PrincipalRecord>();
        return (principal, repair);
    }
}
