using KeyLoad.Clustering;

namespace KeyLoad.UnitTests;

/// <summary>AC-REP-006: fixed capacities and inclusive lifetime bounds preserve replay protection.</summary>
internal sealed class ReplicaReplayLifetimeTests
{
    /// <summary>AC-REP-006: expiry and future timestamp limits use the real system clock and genuine signatures.</summary>
    [Test]
    public async Task StaleAndTooFutureEnvelopesFailWhileAllowedFutureNonceStaysRetained()
    {
        using var fixture = new ReplicaSecurityFixture();
        var now = TimeProvider.System.GetUtcNow().ToUnixTimeMilliseconds();
        var lifetime = checked((long)UnitRoutingOptions.Transport().Value.EnvelopeLifetime.TotalMilliseconds);
        var request = fixture.Vote();
        var expired = fixture.Resign(request with
        {
            Timestamp = now - lifetime - ReplicaSecurityFixture.TimeMarginMilliseconds
        });
        var future = fixture.Resign(request with
        {
            Timestamp = now + lifetime + ReplicaSecurityFixture.TimeMarginMilliseconds
        });
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(expired)).Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(future)).Code).IsEqualTo(ErrorCode.Unauthenticated);
        var accepted = fixture.Resign(request with
        {
            Timestamp = now + lifetime / 2
        });
        fixture.Receiver.VerifyRequest(accepted);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(accepted)).Code).IsEqualTo(ErrorCode.Unauthenticated);
        var window = new ReplicaReplayWindow(fixture.Configuration.VoterIds, UnitRoutingOptions.Replay(fixture.Options.ReplayLimits), UnitRoutingOptions.Transport());
        window.Admit(accepted.Sender, accepted.Nonce, accepted.Timestamp, now, ReplicaReplayPool.Critical);
        var expiry = accepted.Timestamp + lifetime;
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => window.Admit(accepted.Sender, accepted.Nonce,
            expiry, expiry, ReplicaReplayPool.Forward)).Code).IsEqualTo(ErrorCode.Unauthenticated);
        window.Admit(accepted.Sender, accepted.Nonce, expiry + 1, expiry + 1, ReplicaReplayPool.Forward);
    }

    /// <summary>AC-REP-006: zero and oversized configured capacities fail before transport publication.</summary>
    [Test]
    public async Task ReplayConfigurationRejectsNonpositiveAndUnboundedMemoryBudgets()
    {
        using var fixture = new ReplicaSecurityFixture();
        var zero = fixture.Options with
        {
            ReplayLimits = new()
            {
                CriticalPerVoter = 0
            }
        };
        var oversized = fixture.Options with
        {
            ReplayLimits = new()
            {
                ForwardPerVoter = int.MaxValue
            }
        };
        Assert.ThrowsExactly<InvalidOperationException>(() => zero.Validate(fixture.Configuration));
        Assert.ThrowsExactly<InvalidOperationException>(() => oversized.Validate(fixture.Configuration));
        await Assert.That(fixture.Options.ReplayLimits.MaximumRetainedNonces(fixture.Configuration.VoterIds.Length))
            .IsEqualTo(ReplicaSecurityFixture.ReservedCapacity * fixture.Configuration.VoterIds.Length + fixture.Configuration.VoterIds.Length * 3);
    }
}
