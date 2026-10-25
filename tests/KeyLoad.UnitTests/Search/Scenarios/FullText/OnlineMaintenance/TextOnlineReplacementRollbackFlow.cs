using KeyLoad.Clustering.Search;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Search;

internal static class TextOnlineReplacementRollbackFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        TestDatabase? fixture = null;
        var failures = new List<Exception>();
        try
        {
            try
            {
                fixture = new TestDatabase();
                await RunOwnedAsync(fixture, failures, token);
            }
            catch (Exception primary) { failures.Add(primary); throw; }
            finally { fixture?.Dispose(); }
        }
        catch (Exception cleanup) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup))
        {
            if (!failures.Any(error => ReferenceEquals(error, cleanup)))
            {
                failures.Add(cleanup);
            }
        }
        catch (Exception cleanup) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup))
        {
            if (!failures.Any(error => ReferenceEquals(error, cleanup)))
            {
                failures.Add(cleanup);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunOwnedAsync(TestDatabase fixture, List<Exception> failures, CancellationToken token)
    {
        var runtime = new TextOnlineTestRuntime(fixture);
        (OnlineTextIndexMaintenanceRequest Initial, OnlineTextIndexMaintenanceResult Original,
            OnlineTextIndexMaintenanceRequest Fresh, OnlineTextIndexMaintenanceResult Published)? completed = null;
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                completed = await ReplacementAsync(fixture, runtime, token);
            }, failures);
        }
        finally { await runtime.DisposeAsync(); }
        if (failures.Count != 0 || completed is not { } receipts)
        {
            return;
        }
        var cold = new TextOnlineTestRuntime(fixture);
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await TextBilingualAudit.VerifyAsync(cold.Search, fixture, token);
                var oldReplay = await TextOnlineWholeFlow.RunAsync(fixture, cold, receipts.Initial, token);
                await Assert.That(OrleansSerialization.Serialize(oldReplay).SequenceEqual(
                    OrleansSerialization.Serialize(receipts.Original))).IsTrue();
                var freshReplay = await TextOnlineWholeFlow.RunAsync(fixture, cold, receipts.Fresh, token);
                await Assert.That(OrleansSerialization.Serialize(freshReplay).SequenceEqual(
                    OrleansSerialization.Serialize(receipts.Published))).IsTrue();
                await TextBilingualAudit.VerifyAsync(cold.Search, fixture, token);
            }, failures);
        }
        finally { await cold.DisposeAsync(); }
    }

    private static async Task<(OnlineTextIndexMaintenanceRequest Initial, OnlineTextIndexMaintenanceResult Original,
        OnlineTextIndexMaintenanceRequest Fresh, OnlineTextIndexMaintenanceResult Published)> ReplacementAsync(
        TestDatabase fixture, TextOnlineTestRuntime runtime, CancellationToken token)
    {
        var seed = await TextOnlineSeedFixture.CreateAsync(fixture, token);
        var initial = new OnlineTextIndexMaintenanceRequest(Guid.NewGuid(), seed.Pin.Consumer,
            seed.Pin.Collection, seed.Pin.Field, seed.Pin.Generation, seed.Pin.NodeId, seed.Pin.Placement);
        var original = await TextOnlineWholeFlow.RunAsync(fixture, runtime, initial, token);
        var current = ReadCurrent(fixture, initial, token);
        await AbortCapturedAsync(fixture, runtime, initial with
        {
            CommandId = Guid.NewGuid()
        }, token);
        await Assert.That(ReadCurrent(fixture, initial, token).SequenceEqual(current)).IsTrue();
        await TextBilingualAudit.VerifyAsync(runtime.Search, fixture, token);
        var oldReplay = await TextOnlineWholeFlow.RunAsync(fixture, runtime, initial, token);
        await Assert.That(OrleansSerialization.Serialize(oldReplay).SequenceEqual(OrleansSerialization.Serialize(original))).IsTrue();
        var fresh = initial with
        {
            CommandId = Guid.NewGuid()
        };
        var published = await TextOnlineWholeFlow.RunAsync(fixture, runtime, fresh, token);
        await TextBilingualAudit.VerifyAsync(runtime.Search, fixture, token);
        return (initial, original, fresh, published);
    }

    private static async Task AbortCapturedAsync(TestDatabase fixture, TextOnlineTestRuntime runtime,
        OnlineTextIndexMaintenanceRequest replacement, CancellationToken token)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var session = Guid.NewGuid();
        var expiry = TextOnlineTestRuntime.FreshExpiry(fixture);
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
        var budget = new ReadExecutionBudget(fixture.Database.EvaluationClock, token);
        return fixture.Store.Read(view =>
        {
            var principal = fixture.Database.Principal(view, TextMaintenanceTestValues.Principal,
                fixture.Database.EvaluationClock.GetUtcNow());
            var current = fixture.Database.ReadOnlineTextCurrentPublication(view, principal, request, budget)
                ?? throw new InvalidOperationException("The original active online publication is absent.");
            return OrleansSerialization.Serialize(current);
        });
    }
}
