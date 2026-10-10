using KeyLoad.Core;
using KeyLoad.Orleans.Features.Search;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextOnlineReplacementRollbackFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        TestDatabase? fixture = null;
        var failures = new List<Exception>();
        try
        {
            try
            {
                fixture = new TestDatabase(nativeReplicaAdmission: true);
                await RunOwnedAsync(fixture, failures, token);
            }
            catch (Exception primary) { failures.Add(primary); throw; }
            finally { fixture?.Dispose(); }
        }
        catch (Exception cleanup) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
        {
            if (!failures.Any(error => ReferenceEquals(error, cleanup)))
            { failures.Add(cleanup); }
        }
        catch (Exception cleanup) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
        {
            if (!failures.Any(error => ReferenceEquals(error, cleanup)))
            { failures.Add(cleanup); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunOwnedAsync(TestDatabase fixture, List<Exception> failures, CancellationToken token)
    {
        var runtime = new NativeTextOnlineTestRuntime(fixture);
        (OnlineTextIndexMaintenanceRequest Initial, OnlineTextIndexMaintenanceResult Original,
            OnlineTextIndexMaintenanceRequest Fresh, OnlineTextIndexMaintenanceResult Published)? completed = null;
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            { completed = await ReplacementAsync(fixture, runtime, token); }, failures);
        }
        finally { await runtime.DisposeAsync(); }
        if (failures.Count != 0 || completed is not { } receipts)
        { return; }
        var cold = new NativeTextOnlineTestRuntime(fixture);
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await NativeTextBilingualAudit.VerifyAsync(cold.Search, fixture.Partition, token);
                var oldReplay = await NativeTextOnlineWholeFlow.RunAsync(fixture, cold, receipts.Initial, token);
                await Assert.That(NativeSerialization.Serialize(oldReplay).SequenceEqual(
                    NativeSerialization.Serialize(receipts.Original))).IsTrue();
                var freshReplay = await NativeTextOnlineWholeFlow.RunAsync(fixture, cold, receipts.Fresh, token);
                await Assert.That(NativeSerialization.Serialize(freshReplay).SequenceEqual(
                    NativeSerialization.Serialize(receipts.Published))).IsTrue();
                await NativeTextBilingualAudit.VerifyAsync(cold.Search, fixture.Partition, token);
            }, failures);
        }
        finally { await cold.DisposeAsync(); }
    }

    private static async Task<(OnlineTextIndexMaintenanceRequest Initial, OnlineTextIndexMaintenanceResult Original,
        OnlineTextIndexMaintenanceRequest Fresh, OnlineTextIndexMaintenanceResult Published)> ReplacementAsync(
        TestDatabase fixture, NativeTextOnlineTestRuntime runtime, CancellationToken token)
    {
        var seed = await NativeTextOnlineSeedFixture.CreateAsync(fixture, token);
        var initial = new OnlineTextIndexMaintenanceRequest(Guid.NewGuid(), seed.Pin.Consumer,
            seed.Pin.Collection, seed.Pin.Field, seed.Pin.Generation, seed.Pin.NodeId, seed.Pin.Placement);
        var original = await NativeTextOnlineWholeFlow.RunAsync(fixture, runtime, initial, token);
        var current = ReadCurrent(fixture, initial, token);
        await AbortCapturedAsync(fixture, runtime, initial with { CommandId = Guid.NewGuid() }, token);
        await Assert.That(ReadCurrent(fixture, initial, token).SequenceEqual(current)).IsTrue();
        await NativeTextBilingualAudit.VerifyAsync(runtime.Search, fixture.Partition, token);
        var oldReplay = await NativeTextOnlineWholeFlow.RunAsync(fixture, runtime, initial, token);
        await Assert.That(NativeSerialization.Serialize(oldReplay).SequenceEqual(NativeSerialization.Serialize(original))).IsTrue();
        var fresh = initial with { CommandId = Guid.NewGuid() };
        var published = await NativeTextOnlineWholeFlow.RunAsync(fixture, runtime, fresh, token);
        await NativeTextBilingualAudit.VerifyAsync(runtime.Search, fixture.Partition, token);
        return (initial, original, fresh, published);
    }

    private static async Task AbortCapturedAsync(TestDatabase fixture, NativeTextOnlineTestRuntime runtime,
        OnlineTextIndexMaintenanceRequest replacement, CancellationToken token)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var session = Guid.NewGuid();
        var expiry = runtime.FreshExpiry(fixture);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            _ = await runtime.PhaseAsync(fixture, session, replacement, OnlineTextCapabilityKind.ResolveOriginal,
                expiry, cancellation.Token);
            _ = await runtime.PhaseAsync(fixture, session, replacement, OnlineTextCapabilityKind.Capture,
                expiry, cancellation.Token);
            await cancellation.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => runtime.PhaseAsync(fixture, session,
                replacement, OnlineTextCapabilityKind.Seed, expiry, cancellation.Token));
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => runtime.Owner.AbortAsync(session), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static byte[] ReadCurrent(TestDatabase fixture, OnlineTextIndexMaintenanceRequest request, CancellationToken token)
    {
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(fixture.Database.Limits),
            fixture.Database.EvaluationClock, token);
        return fixture.Store.Read(view =>
        {
            var principal = fixture.Database.Principal(view, NativeTextMaintenanceTestValues.Principal,
                fixture.Database.EvaluationClock.GetUtcNow());
            var current = fixture.Database.ReadOnlineTextCurrentPublication(view, principal, request, budget)
                ?? throw new InvalidOperationException("The original active online publication is absent.");
            return NativeSerialization.Serialize(current);
        });
    }
}
