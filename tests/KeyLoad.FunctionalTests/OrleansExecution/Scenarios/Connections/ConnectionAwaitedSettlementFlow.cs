namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class ConnectionAwaitedSettlementFlow
{
    internal const string CleanupFailure = "execution-awaited-notification-failure";
    private const long FirstRevision = 1;

    internal static async Task<Guid> SuccessAsync(ConnectionScenario scenario,
        ConnectionAwaitedSettlementObservation observer, PrincipalRecord principal, string document, CancellationToken token)
    {
        var requestId = Guid.NewGuid();
        var callback = observer.Hold(requestId);
        var command = scenario.Command(document);
        var original = ConnectionAwaitedSettlementCalls.CommandAsync(scenario, principal, requestId, command, token);
        Exception? primary = null;
        try
        {
            await callback.Arrived.Task.WaitAsync(token);
            await ConnectionAwaitedSettlementAssertions.ChargedAsync(scenario.Observation.Read(requestId).WorkOwner, requestId);
            callback.Release.SetResult();
            await Assert.That((await original).Error).IsNull();
            await ConnectionAwaitedSettlementAssertions.ReleasedAsync(scenario.Observation.Read(requestId).WorkOwner, requestId, token);
            await ConnectionAssertions.StoredAsync(scenario, document, FirstRevision, ConnectionProtocol.FirstJson);
            await Assert.That(scenario.Observation.Read(requestId).Identity.PrincipalId).IsEqualTo(principal.Id);
            return requestId;
        }
        catch (Exception error) { primary = error; throw; }
        finally { callback.Release.TrySetResult(); await ConnectionAwaitedSettlementCalls.JoinOriginalAsync(original, primary); }
    }

    internal static async Task<Guid> CancelAsync(ConnectionScenario scenario,
        ConnectionAwaitedSettlementObservation observer, PrincipalRecord principal, CancellationToken token)
    {
        var requestId = Guid.NewGuid();
        var phase = scenario.Observation.Hold(requestId);
        var callback = observer.Hold(requestId);
        var command = scenario.Command("awaited-cancelled");
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
        var original = ConnectionAwaitedSettlementCalls.CommandAsync(scenario, principal, requestId, command, caller.Token);
        Exception? primary = null;
        try
        {
            await phase.Arrived.Task.WaitAsync(token);
            await caller.CancelAsync();
            await callback.Arrived.Task.WaitAsync(token);
            await ConnectionAwaitedSettlementAssertions.ChargedAsync(scenario.Observation.Read(requestId).WorkOwner, requestId);
            callback.Release.SetResult();
            await ConnectionAssertions.CancelledAsync(original);
            await ConnectionAwaitedSettlementAssertions.ReleasedAsync(scenario.Observation.Read(requestId).WorkOwner, requestId, token);
            await ConnectionAssertions.MissingOutcomeAsync(scenario, principal, command, "awaited-cancelled");
            return requestId;
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            phase.Release.TrySetResult();
            callback.Release.TrySetResult();
            await ConnectionAwaitedSettlementCalls.JoinOriginalAsync(original, primary, allowCancellation: true);
        }
    }

    internal static async Task<Guid> EarlyAsync(ConnectionScenario scenario,
        ConnectionAwaitedSettlementObservation observer, PrincipalRecord principal, CancellationToken token)
    {
        var requestId = Guid.NewGuid();
        var phase = scenario.Observation.Hold(requestId);
        var callback = observer.Hold(requestId);
        var command = scenario.Command("awaited-early");
        var original = ConnectionAwaitedSettlementCalls.EarlyDisposeAsync(scenario, principal, requestId, command, token);
        Exception? primary = null;
        try
        {
            await callback.Arrived.Task.WaitAsync(token);
            await ConnectionAwaitedSettlementAssertions.ChargedAsync(scenario.Observation.Read(requestId).WorkOwner, requestId);
            callback.Release.SetResult();
            await original;
            await ConnectionAwaitedSettlementAssertions.ReleasedAsync(scenario.Observation.Read(requestId).WorkOwner, requestId, token);
            await ConnectionAssertions.MissingOutcomeAsync(scenario, principal, command, "awaited-early");
            return requestId;
        }
        catch (Exception error) { primary = error; throw; }
        finally { phase.Release.TrySetResult(); callback.Release.TrySetResult(); await ConnectionAwaitedSettlementCalls.JoinOriginalAsync(original, primary); }
    }

    internal static async Task<Guid> FaultAsync(ConnectionScenario scenario,
        ConnectionAwaitedSettlementObservation observer, PrincipalRecord principal, CancellationToken token)
    {
        var requestId = Guid.NewGuid();
        var callback = observer.Hold(requestId, new IOException(CleanupFailure));
        var original = ConnectionAwaitedSettlementCalls.CommandAsync(scenario, principal, requestId,
            scenario.Command("awaited-callback-fault"), token);
        Exception? primary = null;
        try
        {
            await callback.Arrived.Task.WaitAsync(token);
            await ConnectionAwaitedSettlementAssertions.ChargedAsync(scenario.Observation.Read(requestId).WorkOwner, requestId);
            callback.Release.SetResult();
            var failure = (await ConnectionAwaitedSettlementCalls.FailureAsync(original)).Value;
            await Assert.That(failure).IsNotNull();
            await Assert.That(ConnectionAwaitedSettlementAssertions.Contains(failure!, CleanupFailure)).IsTrue();
            await ConnectionAwaitedSettlementAssertions.ReleasedAsync(scenario.Observation.Read(requestId).WorkOwner, requestId, token);
            await ConnectionAssertions.StoredAsync(scenario, "awaited-callback-fault", FirstRevision, ConnectionProtocol.FirstJson);
            return requestId;
        }
        catch (Exception error) { primary = error; throw; }
        finally { callback.Release.TrySetResult(); await ConnectionAwaitedSettlementCalls.JoinOriginalAsync(original, primary, expectedFailure: true); }
    }
}
