using KeyLoad.Orleans;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class ConnectionNativeFailureTests
{
    [Test]
    public Task AcCrs052FailureAndRevocationLeaveNoEffectsAndFollowingCallSucceeds()
    {
        return ConnectionNativeScenario.RunAsync(async scenario =>
        {
            using var timeout = new CancellationTokenSource(scenario.Timing.CompletionTimeout, TimeProvider.System);
            var token = timeout.Token;
            await scenario.InitializeAsync(token);
            var principal = scenario.Principal();
            var failedId = Guid.NewGuid();
            scenario.Observation.Hold(failedId, fail: true);
            var failedCommand = scenario.Command("ordinary-failure");
            var failed = await scenario.CommandAsync(principal, failedId, failedCommand, token);
            await Assert.That(failed.Error).IsEqualTo(ErrorCode.UnknownWriteOutcome);
            await ConnectionNativeAssertions.MissingOutcomeAsync(scenario, principal, failedCommand, "ordinary-failure");
            var revoked = principal with { Revoked = true, PolicyEpoch = principal.PolicyEpoch + 1 };
            scenario.Fixture.Database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(revoked));
            var rejectedId = Guid.NewGuid();
            var rejectedCommand = scenario.Command("revoked-operation");
            var position = scenario.Fixture.Database.Store.Position;
            var rejected = await scenario.CommandAsync(principal, rejectedId, rejectedCommand, token);
            await Assert.That(rejected.Error).IsEqualTo(ErrorCode.Unauthenticated);
            await Assert.That(scenario.Fixture.Database.Store.Position).IsEqualTo(position);
            await ConnectionNativeAssertions.MissingOutcomeAsync(scenario, principal, rejectedCommand, "revoked-operation");
            var following = scenario.Principal();
            var followingId = Guid.NewGuid();
            var command = scenario.Command("healthy-after-revocation");
            var reply = await scenario.CommandAsync(following, followingId, command, token);
            await Assert.That(reply.Error).IsNull();
            await ConnectionNativeAssertions.StoredAsync(scenario, "healthy-after-revocation", 1, ConnectionNativeProtocol.FirstJson);
            await ConnectionNativeAssertions.SameOwnerAsync(scenario, failedId, followingId, token);
            await ConnectionNativeAssertions.SameOwnerAsync(scenario, rejectedId, followingId, token);
            await scenario.CloseAsync(token);
        });
    }

    [Test]
    public Task AcCrs053OperationCapacityRejectsWithoutQueueAndRecoversAfterSettlement()
    {
        var routing = new GrainRoutingOptions { MaximumConnectionOperations = 1 };
        return ConnectionNativeScenario.RunAsync(async scenario =>
        {
            using var timeout = new CancellationTokenSource(scenario.Timing.CompletionTimeout, TimeProvider.System);
            var token = timeout.Token;
            await scenario.InitializeAsync(token);
            var principal = scenario.Principal();
            var heldId = Guid.NewGuid();
            var held = scenario.Observation.Hold(heldId);
            var original = scenario.Command("capacity-original");
            var first = scenario.CommandAsync(principal, heldId, original, token);
            await held.Arrived.Task.WaitAsync(token);
            var rejected = scenario.Command("capacity-rejected");
            var failure = await Assert.ThrowsAsync<KeyLoadException>(
                () => scenario.CommandAsync(principal, Guid.NewGuid(), rejected, token));
            await Assert.That(failure).IsNotNull();
            await Assert.That(failure!.Code).IsEqualTo(ErrorCode.ResourceExhausted);
            await Assert.That(first.IsCompleted).IsFalse();
            await ConnectionNativeAssertions.MissingOutcomeAsync(scenario, principal, rejected, "capacity-rejected");
            held.Release.TrySetResult();
            await Assert.That((await first).Error).IsNull();
            await scenario.Observation.Read(heldId).Disposed.Task.WaitAsync(token);
            var recoveredId = Guid.NewGuid();
            var recovered = await scenario.CommandAsync(principal, recoveredId, rejected, token);
            await Assert.That(recovered.Error).IsNull();
            await ConnectionNativeAssertions.StoredAsync(scenario, "capacity-original", 1, ConnectionNativeProtocol.FirstJson);
            await ConnectionNativeAssertions.StoredAsync(scenario, "capacity-rejected", 1, ConnectionNativeProtocol.FirstJson);
            await ConnectionNativeAssertions.SameOwnerAsync(scenario, heldId, recoveredId, token);
            await scenario.CloseAsync(token);
        }, routing);
    }

    [Test]
    public Task AcCrs054ConnectionCloseJoinsOriginalCallerBeforeSignedNativeRemoval()
    {
        return ConnectionNativeScenario.RunAsync(async scenario =>
        {
            using var timeout = new CancellationTokenSource(scenario.Timing.CompletionTimeout, TimeProvider.System);
            var token = timeout.Token;
            await scenario.InitializeAsync(token);
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
            await scenario.AssertActivationCountAsync(ConnectionNativeProtocol.OneConnection, token);
            await scenario.CloseAsync(token);
            await ConnectionNativeAssertions.CancelledAsync(original);
            await scenario.Observation.Read(requestId).Disposed.Task.WaitAsync(token);
            await ConnectionNativeAssertions.MissingOutcomeAsync(scenario, principal, command, "closed-original");
            await scenario.AssertActivationCountAsync(ConnectionNativeProtocol.AbsentActivations, token);
        });
    }

    [Test]
    public Task AcCrs060NativeExceptionSerializationPreservesDomainFailureAndCause()
    {
        return ConnectionNativeScenario.RunAsync(async scenario =>
        {
            using var timeout = new CancellationTokenSource(scenario.Timing.CompletionTimeout, TimeProvider.System);
            await scenario.InitializeAsync(timeout.Token);
            var serializer = scenario.Fixture.Cluster.ServiceProvider
                .GetRequiredService<global::Orleans.Serialization.Serializer<Exception>>();
            KeyLoadException[] originals =
            [
                new(ErrorCode.ResourceExhausted, ConnectionNativeProtocol.OrdinaryFailure,
                    Errors.Status(ErrorCode.ResourceExhausted)),
                new(ConnectionNativeProtocol.OrdinaryFailure,
                    new IOException(ConnectionNativeProtocol.OrdinaryFailure))
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
