using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class ConnectionNativeCloseAdmissionFlow
{
    private const long InitialRevision = 1;

    internal static async Task RunAsync(ConnectionNativeScenario scenario,
        ConnectionAwaitedSettlementObservation observer, CancellationToken token)
    {
        var first = scenario.Principal();
        var second = scenario.Principal();
        var requestId = Guid.NewGuid();
        var callback = observer.Hold(requestId);
        var command = scenario.Command("close-admission-original");
        var original = ConnectionAwaitedSettlementCalls.CommandAsync(scenario, first, requestId, command, token);
        Task? close = null;
        var failures = new List<Exception>();
        try
        {
            await callback.Arrived.Task.WaitAsync(token);
            await ConnectionAwaitedSettlementAssertions.ChargedAsync(scenario.Fixture.RequestWork, requestId);
            await Assert.That(original.IsCompleted).IsFalse();
            await RefusalsAsync(scenario, second, token);
            close = scenario.CloseOwnerAsync(token);
            await ClosedAdmissionAsync(scenario, second, token);
            await Assert.That(original.IsCompleted).IsFalse();
            await Assert.That(close.IsCompleted).IsFalse();
            await ConnectionAwaitedSettlementAssertions.ChargedAsync(scenario.Fixture.RequestWork, requestId);
            callback.Release.TrySetResult();
            var receipt = await original;
            await Assert.That(receipt.Error).IsNull();
            await close;
            await ConnectionNativeAssertions.StoredAsync(scenario, "close-admission-original", InitialRevision,
                ConnectionNativeProtocol.FirstJson);
            await scenario.AssertActivationCountAsync(ConnectionNativeProtocol.AbsentActivations, token);
            await FreshOwnerAsync(scenario, observer, second, requestId, token);
        }
        catch (Exception primary) when (global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(primary))
        { failures.Add(primary); }
        catch (Exception primary) when (!global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(primary))
        { failures.Add(primary); }
        finally
        {
            callback.Release.TrySetResult();
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => original, failures);
            if (close is not null)
            { await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => close, failures); }
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RefusalsAsync(ConnectionNativeScenario scenario, PrincipalRecord second,
        CancellationToken token)
    {
        var command = scenario.Command("close-admission-refused");
        var position = scenario.Fixture.Database.Store.Position;
        var refused = await Assert.ThrowsAsync<KeyLoadException>(() =>
            ConnectionAwaitedSettlementCalls.CommandAsync(scenario, second, Guid.NewGuid(), command, token));
        await Assert.That(refused).IsNotNull();
        await Assert.That(refused!.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token);
        await cancelled.CancelAsync();
        await ConnectionNativeAssertions.CancelledAsync(ConnectionAwaitedSettlementCalls.CommandAsync(
            scenario, second, Guid.NewGuid(), command, cancelled.Token));
        await Assert.That(scenario.Fixture.Database.Store.Position).IsEqualTo(position);
        await ConnectionNativeAssertions.MissingOutcomeAsync(scenario, second, command, "close-admission-refused");
    }

    private static async Task ClosedAdmissionAsync(ConnectionNativeScenario scenario,
        PrincipalRecord second, CancellationToken token)
    {
        var command = scenario.Command("close-admission-closed");
        var position = scenario.Fixture.Database.Store.Position;
        var rejected = await Assert.ThrowsAsync<KeyLoadException>(() =>
            ConnectionAwaitedSettlementCalls.CommandAsync(scenario, second, Guid.NewGuid(), command, token));
        await Assert.That(rejected).IsNotNull();
        await Assert.That(rejected!.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(scenario.Fixture.Database.Store.Position).IsEqualTo(position);
        await ConnectionNativeAssertions.MissingOutcomeAsync(scenario, second, command, "close-admission-closed");
    }

    private static async Task FreshOwnerAsync(ConnectionNativeScenario scenario,
        ConnectionAwaitedSettlementObservation observer, PrincipalRecord second, Guid previous, CancellationToken token)
    {
        var ownerId = Guid.NewGuid();
        var owner = scenario.Fixture.Cluster.Client.GetGrain<IConnectionGrain>(ownerId);
        var requestId = Guid.NewGuid();
        var command = scenario.Command("close-admission-healthy", ConnectionNativeProtocol.SecondJson);
        var failures = new List<Exception>();
        try
        {
            var created = await ConnectionAwaitedSettlementCalls.CommandForOwnerAsync(scenario, second, requestId,
                command, ownerId, token);
            await Assert.That(created.Error).IsNull();
            await Assert.That(observer.Identity(requestId).ConnectionId).IsEqualTo(ownerId);
            await Assert.That(scenario.Observation.Read(requestId).Identity.PrincipalId).IsEqualTo(second.Id);
            await Assert.That(scenario.Observation.Read(requestId).GrainId)
                .IsNotEqualTo(scenario.Observation.Read(previous).GrainId);
            await ConnectionNativeAssertions.StoredAsync(scenario, "close-admission-healthy", InitialRevision,
                ConnectionNativeProtocol.SecondJson);
            var position = scenario.Fixture.Database.Store.Position;
            var replay = await ConnectionAwaitedSettlementCalls.CommandForOwnerAsync(scenario, second, Guid.NewGuid(),
                command, ownerId, token);
            await Assert.That(replay.Error).IsNull();
            await Assert.That(replay.Payload.Span.SequenceEqual(created.Payload.Span)).IsTrue();
            await Assert.That(scenario.Fixture.Database.Store.Position).IsEqualTo(position);
        }
        catch (Exception primary) when (global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(primary))
        { failures.Add(primary); }
        catch (Exception primary) when (!global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(primary))
        { failures.Add(primary); }
        finally
        {
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(
                () => owner.CloseAsync(scenario.Fixture.Codec.CreateConnectionClose(ownerId), token), failures);
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        await scenario.AssertActivationCountAsync(ConnectionNativeProtocol.AbsentActivations, token);
    }
}
