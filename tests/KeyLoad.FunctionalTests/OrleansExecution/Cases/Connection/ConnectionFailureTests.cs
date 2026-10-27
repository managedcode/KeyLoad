using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class ConnectionFailureTests
{
    [Test]
    [NotInParallel]
    public Task AcCrs052FailureAndRevocationLeaveNoEffectsAndFollowingCallSucceeds()
    {
        return ConnectionScenario.RunAsync(async scenario =>
        {
            using var timeout = new CancellationTokenSource(scenario.Timing.StartupTimeout, TimeProvider.System);
            using var caller = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current!.Execution.CancellationToken, timeout.Token);
            var token = caller.Token;
            await scenario.InitializeAsync(token);
            timeout.CancelAfter(scenario.Timing.CompletionTimeout);
            var principal = scenario.Principal();
            using var diagnostics = new EventSourceLogCapture();
            var beforeFailure = scenario.Fixture.Database.ReadCanonicalCut();
            var beforeFailurePosition = scenario.Fixture.Database.Store.Position;
            var failedId = Guid.NewGuid();
            scenario.Observation.Hold(failedId, fail: true);
            var failedCommand = scenario.Command("ordinary-failure");
            var failed = await scenario.CommandAsync(principal, failedId, failedCommand, token);
            await Assert.That(failed.Error).IsEqualTo(ErrorCode.UnknownWriteOutcome);
            await ConnectionFailureDiagnosticAssertions.FailedAsync(scenario, failedId, failed, diagnostics, token);
            await Assert.That(scenario.Fixture.Database.ReadCanonicalCut()).IsEqualTo(beforeFailure);
            await Assert.That(scenario.Fixture.Database.Store.Position).IsEqualTo(beforeFailurePosition);
            await ConnectionAssertions.MissingOutcomeAsync(scenario, principal, failedCommand, "ordinary-failure");
            var revoked = principal with
            {
                Revoked = true,
                PolicyEpoch = principal.PolicyEpoch + 1
            };
            scenario.Fixture.Database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(revoked));
            var rejectedId = Guid.NewGuid();
            var rejectedCommand = scenario.Command("revoked-operation");
            var position = scenario.Fixture.Database.Store.Position;
            var rejected = await scenario.CommandAsync(principal, rejectedId, rejectedCommand, token);
            await Assert.That(rejected.Error).IsEqualTo(ErrorCode.Unauthenticated);
            await Assert.That(scenario.Fixture.Database.Store.Position).IsEqualTo(position);
            await ConnectionAssertions.MissingOutcomeAsync(scenario, principal, rejectedCommand, "revoked-operation");
            var following = scenario.Principal();
            var followingId = Guid.NewGuid();
            var command = scenario.Command("healthy-after-revocation");
            var reply = await scenario.CommandAsync(following, followingId, command, token);
            await Assert.That(reply.Error).IsNull();
            await ConnectionAssertions.StoredAsync(scenario, "healthy-after-revocation", 1, ConnectionProtocol.FirstJson);
            await ConnectionAssertions.SameOwnerAsync(scenario, failedId, followingId, token);
            await ConnectionAssertions.SameOwnerAsync(scenario, rejectedId, followingId, token);
            await ConnectionFailureDiagnosticAssertions.ReplayAsync(scenario, following, followingId, command, reply, token);
            await scenario.CloseAsync(token);
        });
    }

    [Test]
    public Task AcCrs053ConnectionRunsAnotherOperationWhileTheFirstIsHeld()
    {
        return ConnectionScenario.RunAsync(async scenario =>
        {
            using var timeout = new CancellationTokenSource(scenario.Timing.StartupTimeout, TimeProvider.System);
            using var caller = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current!.Execution.CancellationToken, timeout.Token);
            var token = caller.Token;
            await scenario.InitializeAsync(token);
            timeout.CancelAfter(scenario.Timing.CompletionTimeout);
            var principal = scenario.Principal();
            var heldId = Guid.NewGuid();
            var held = scenario.Observation.Hold(heldId);
            var original = scenario.Command("held-original");
            var first = scenario.CommandAsync(principal, heldId, original, token);
            await held.Arrived.Task.WaitAsync(token);
            var concurrentId = Guid.NewGuid();
            var concurrent = await scenario.CommandAsync(principal, concurrentId, scenario.Command("concurrent-second"), token);
            await Assert.That(concurrent.Error).IsNull();
            await Assert.That(first.IsCompleted).IsFalse();
            await ConnectionAssertions.StoredAsync(scenario, "concurrent-second", 1, ConnectionProtocol.FirstJson);
            held.Release.TrySetResult();
            await Assert.That((await first).Error).IsNull();
            await scenario.Observation.Read(heldId).Disposed.Task.WaitAsync(token);
            await ConnectionAssertions.StoredAsync(scenario, "held-original", 1, ConnectionProtocol.FirstJson);
            await ConnectionAssertions.SameOwnerAsync(scenario, heldId, concurrentId, token);
            await scenario.CloseAsync(token);
        });
    }

    [Test]
    public Task AcCrs054ConnectionCloseJoinsOriginalCallerBeforeSignedRemoval()
    {
        return ConnectionScenario.RunAsync(async scenario =>
        {
            using var timeout = new CancellationTokenSource(scenario.Timing.StartupTimeout, TimeProvider.System);
            using var caller = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current!.Execution.CancellationToken, timeout.Token);
            var token = caller.Token;
            await scenario.InitializeAsync(token);
            timeout.CancelAfter(scenario.Timing.CompletionTimeout);
            var principal = scenario.Principal();
            var requestId = Guid.NewGuid();
            var held = scenario.Observation.Hold(requestId);
            var command = scenario.Command("closed-original");
            var original = scenario.CommandAsync(principal, requestId, command, token);
            await held.Arrived.Task.WaitAsync(token);
            var wrongOwner = scenario.Fixture.Codec.CreateConnectionClose(Guid.NewGuid());
            var denied = await Assert.ThrowsAsync<KeyLoadException>(() => scenario.Connection.CloseAsync(wrongOwner, token));
            await Assert.That(denied).IsNotNull();
            await Assert.That(denied!.Code).IsEqualTo(ErrorCode.TokenInvalidated);
            await Assert.That(original.IsCompleted).IsFalse();
            await scenario.AssertActivationCountAsync(ConnectionProtocol.OneConnection, token);
            await scenario.CloseAsync(token);
            await ConnectionAssertions.CancelledAsync(original);
            await scenario.Observation.Read(requestId).Disposed.Task.WaitAsync(token);
            await ConnectionAssertions.MissingOutcomeAsync(scenario, principal, command, "closed-original");
            await scenario.AssertActivationCountAsync(ConnectionProtocol.AbsentActivations, token);
            await ConnectionClosedCommandContinuation.RunAsync(scenario, principal, command, requestId, token);
        });
    }

    [Test]
    public Task AcCrs060ExceptionSerializationPreservesDomainFailureAndCause()
    {
        return ConnectionScenario.RunAsync(async scenario =>
        {
            using var timeout = new CancellationTokenSource(scenario.Timing.StartupTimeout, TimeProvider.System);
            using var caller = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current!.Execution.CancellationToken, timeout.Token);
            await scenario.InitializeAsync(caller.Token);
            timeout.CancelAfter(scenario.Timing.CompletionTimeout);
            var serializer = scenario.Fixture.Cluster.ServiceProvider
                .GetRequiredService<global::Orleans.Serialization.Serializer<Exception>>();
            KeyLoadException[] originals =
            [
                new(ErrorCode.ResourceExhausted, ConnectionProtocol.OrdinaryFailure,
                    Errors.Status(ErrorCode.ResourceExhausted)),
                new(ConnectionProtocol.OrdinaryFailure,
                    new IOException(ConnectionProtocol.OrdinaryFailure))
            ];
            foreach (var original in originals)
            {
                var restored = serializer.Deserialize(serializer.SerializeToArray(original));
                ArgumentNullException.ThrowIfNull(restored);
                await Assert.That(restored.GetType()).IsEqualTo(typeof(KeyLoadException));
                var actual = (KeyLoadException)restored;
                await Assert.That(actual.Code).IsEqualTo(original.Code);
                await Assert.That(actual.StatusCode).IsEqualTo(original.StatusCode);
                await Assert.That(actual.Message).IsEqualTo(original.Message);
                await Assert.That(actual.InnerException?.GetType()).IsEqualTo(original.InnerException?.GetType());
                await Assert.That(actual.InnerException?.Message).IsEqualTo(original.InnerException?.Message);
            }
        });
    }
}
