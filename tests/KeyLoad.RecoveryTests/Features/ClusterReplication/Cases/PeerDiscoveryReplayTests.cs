using KeyLoad.Replication;

namespace KeyLoad.RecoveryTests;

/// <summary>AC-REP-006: authenticated discovery replay state has strict temporal and fixed-capacity boundaries.</summary>
internal sealed class PeerDiscoveryReplayTests
{
    private const int ConcurrentRequests = 8;
    private const int ConcurrentCapacity = 2;

    /// <summary>Expired, too-future and extreme genuinely authenticated timestamps fail without admission or arithmetic overflow.</summary>
    [Test]
    public async Task RealClockRejectsStaleFutureAndExtremeSignedTimestamps()
    {
        using var fixture = new PeerDiscoveryFixture();
        var now = TimeProvider.System.GetUtcNow().ToUnixTimeMilliseconds();
        var window = fixture.TimestampWindowMilliseconds + PeerDiscoveryFixture.TimeMarginMilliseconds;
        foreach (var timestamp in new[] { now - window, now + window, long.MinValue, long.MaxValue })
        {
            using var message = fixture.Sign();
            fixture.Resign(message, timestamp);
            await Assert.That(await fixture.Receiver.ValidateAsync(PeerDiscoveryFixture.Incoming(message), PeerDiscoveryFixture.Cancellation)).IsFalse();
        }
        using var current = fixture.Sign();
        await Assert.That(await fixture.Receiver.ValidateAsync(PeerDiscoveryFixture.Incoming(current), PeerDiscoveryFixture.Cancellation)).IsTrue();
        await Assert.That(PeerDiscoverySignature.Fresh((now - fixture.TimestampWindowMilliseconds).ToString(
            System.Globalization.CultureInfo.InvariantCulture), now, fixture.TimestampWindowMilliseconds, out _)).IsTrue();
        await Assert.That(PeerDiscoverySignature.Fresh((now + fixture.TimestampWindowMilliseconds).ToString(
            System.Globalization.CultureInfo.InvariantCulture), now, fixture.TimestampWindowMilliseconds, out _)).IsTrue();
    }

    /// <summary>A permitted future timestamp retains its nonce through timestamp plus thirty seconds, inclusively.</summary>
    [Test]
    public async Task FutureSignedNonceAndActualReplayAlgorithmPreserveInclusiveExpiry()
    {
        using var fixture = new PeerDiscoveryFixture();
        using var message = fixture.Sign();
        var now = TimeProvider.System.GetUtcNow().ToUnixTimeMilliseconds();
        var timestamp = now + fixture.TimestampWindowMilliseconds / 2;
        fixture.Resign(message, timestamp);
        await Assert.That(await fixture.Receiver.ValidateAsync(PeerDiscoveryFixture.Incoming(message), PeerDiscoveryFixture.Cancellation)).IsTrue();
        await Assert.That(await fixture.Receiver.ValidateAsync(PeerDiscoveryFixture.Incoming(message), PeerDiscoveryFixture.Cancellation)).IsFalse();
        var nonce = Guid.ParseExact(message.Headers.GetValues(PeerDiscoveryProtocol.NonceHeader).Single(), PeerDiscoveryProtocol.NonceFormat);
        var replay = new PeerDiscoveryReplay(PeerDiscoveryFixture.SingleCapacity, fixture.TimestampWindowMilliseconds);
        await Assert.That(replay.Admit(nonce, timestamp, now)).IsTrue();
        var expiry = timestamp + fixture.TimestampWindowMilliseconds;
        var early = now + fixture.TimestampWindowMilliseconds + 1;
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => replay.Admit(Guid.NewGuid(), early, early)).Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(replay.Admit(nonce, expiry, expiry)).IsFalse();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => replay.Admit(Guid.NewGuid(), expiry, expiry)).Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(replay.Admit(nonce, expiry + 1, expiry + 1)).IsTrue();
    }

    /// <summary>Saturation is typed only for otherwise authenticated fresh requests; replays remain false when full.</summary>
    [Test]
    public async Task AuthenticatedCapacityErrorDoesNotHideReplayOrForgedInput()
    {
        using var fixture = new PeerDiscoveryFixture();
        using var first = fixture.Sign();
        using var second = fixture.Sign();
        await Assert.That(await fixture.Receiver.ValidateAsync(PeerDiscoveryFixture.Incoming(first), PeerDiscoveryFixture.Cancellation)).IsTrue();
        var forged = PeerDiscoveryFixture.Incoming(second);
        fixture.Mutate(forged, PeerDiscoveryMutation.Signature);
        await Assert.That(await fixture.Receiver.ValidateAsync(forged, PeerDiscoveryFixture.Cancellation)).IsFalse();
        await Assert.That(await fixture.Receiver.ValidateAsync(PeerDiscoveryFixture.Incoming(first), PeerDiscoveryFixture.Cancellation)).IsFalse();
        var exhausted = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => fixture.Receiver.ValidateAsync(
            PeerDiscoveryFixture.Incoming(second), PeerDiscoveryFixture.Cancellation));
        await Assert.That(exhausted!.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    /// <summary>Parallel actual requests cannot exceed the synchronized fixed-capacity replay table.</summary>
    [Test]
    public async Task ConcurrentActualRequestsRespectFixedAdmissionCapacity()
    {
        using var fixture = new PeerDiscoveryFixture(ConcurrentCapacity);
        var requests = Enumerable.Range(0, ConcurrentRequests).Select(_ => fixture.Sign()).ToArray();
        try
        {
            var calls = requests.Select(message => Task.Run(async () =>
            {
                try
                { return (Accepted: await fixture.Receiver.ValidateAsync(PeerDiscoveryFixture.Incoming(message), PeerDiscoveryFixture.Cancellation), Error: (ErrorCode?)null); }
                catch (KeyLoadException failure) { return (Accepted: false, Error: (ErrorCode?)failure.Code); }
            }, PeerDiscoveryFixture.Cancellation));
            var accepted = await Task.WhenAll(calls);
            await Assert.That(accepted.Count(value => value.Accepted)).IsEqualTo(ConcurrentCapacity);
            await Assert.That(accepted.Where(value => !value.Accepted).All(value => value.Error == ErrorCode.ResourceExhausted)).IsTrue();
        }
        finally
        {
            foreach (var request in requests)
            { request.Dispose(); }
        }
    }
}
