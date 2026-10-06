using System.Collections.Concurrent;
using System.Globalization;
using KeyLoad.Orleans;
using KeyLoad.Replication;
using Microsoft.Extensions.Logging;

namespace KeyLoad.UnitTests;

/// <summary>AC-REP-006: genuine signed admission emits bounded numeric quota evidence without credentials.</summary>
internal sealed class ReplicaReplayDiagnosticsTests
{
    private const int CapacityEvent = 2101;
    private const int ConfigurationEvent = 2102;
    private const int SuppressedCalls = 32;
    private const long IntervalMilliseconds = 30_000;
    private const string SenderIndex = "SenderIndex";
    private const string Pool = "Pool";
    private const string Method = "Method";
    private const string Capacity = "Capacity";
    private const string ReadCount = "ReadCount";
    private const string Suppressed = "Suppressed";

    /// <summary>The rejected signed request retains exact method/pool/count without native identity or secret text.</summary>
    [Test]
    public async Task SignedQuotaDenialEmitsClosedNumericSnapshotAndOriginalError()
    {
        using var fixture = new ReplicaSecurityFixture();
        using var capture = new ReplicaReplayLogCapture();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(capture));
        using var receiver = new ReplicaEnvelopeAuthenticator(fixture.Configuration, fixture.Options,             fixture.Discovery, TimeProvider.System, factory.CreateLogger<ReplicaEnvelopeAuthenticator>(), UnitRoutingOptions.Replay());
        var request = fixture.Request(ReplicaRpc.ReadProbe,
            new AppendRequest(ReplicaSecurityFixture.VoterA, 1, 0, 0, 0, []));
        receiver.VerifyRequest(request);
        var rejected = fixture.Resign(request with { Nonce = Guid.NewGuid() });
        var error = Assert.ThrowsExactly<KeyLoadException>(() => receiver.VerifyRequest(rejected));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(error.Message).IsEqualTo(ReplicaTransportProtocol.ReplayCapacityExceeded);
        var entry = capture.Entries.Single(item => item.Event == CapacityEvent);
        await Assert.That(entry.Number(SenderIndex)).IsEqualTo(0L);
        await Assert.That(entry.Number(Pool)).IsEqualTo((long)ReplicaReplayPool.ReadBarrier);
        await Assert.That(entry.Number(Method)).IsEqualTo((long)ReplicaRpc.ReadProbe);
        await Assert.That(entry.Number(Capacity)).IsEqualTo(1L);
        await Assert.That(entry.Number(ReadCount)).IsEqualTo(1L);
        foreach (var secret in new[] { ReplicaSecurityFixture.VoterA, ReplicaSecurityFixture.VoterB,
            ReplicaSecurityFixture.ClusterId, request.Nonce.ToString(), Convert.ToHexString(fixture.Options.Secret.Span) })
        {
            await Assert.That(entry.Rendered.Contains(secret, StringComparison.Ordinal)).IsFalse();
        }
        await Assert.That(capture.Entries.Count(item => item.Event == ConfigurationEvent)).IsEqualTo(1);
    }

    /// <summary>Replay and tamper remain authentication failures and never produce authenticated quota evidence.</summary>
    [Test]
    public async Task ReplayAndTamperDoNotEmitQuotaRecords()
    {
        using var fixture = new ReplicaSecurityFixture();
        using var capture = new ReplicaReplayLogCapture();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(capture));
        using var receiver = new ReplicaEnvelopeAuthenticator(fixture.Configuration, fixture.Options,             fixture.Discovery, TimeProvider.System, factory.CreateLogger<ReplicaEnvelopeAuthenticator>(), UnitRoutingOptions.Replay());
        var request = fixture.Read();
        receiver.VerifyRequest(request);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => receiver.VerifyRequest(request)).Code)
            .IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => receiver.VerifyRequest(
            request with { Nonce = Guid.NewGuid() })).Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(capture.Entries.Count(item => item.Event == CapacityEvent)).IsEqualTo(0);
    }

    /// <summary>Fixed sender/pool rate state emits first then one summary per interval with an exact suppressed count.</summary>
    [Test]
    public async Task QuotaLoggingIsBoundedAndSuppressionsStayNumeric()
    {
        using var fixture = new ReplicaSecurityFixture();
        using var capture = new ReplicaReplayLogCapture();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(capture));
        var diagnostics = new ReplicaReplayAdmissionDiagnostics(factory.CreateLogger<ReplicaEnvelopeAuthenticator>(),
            fixture.Configuration.VoterIds.Length);
        var window = new ReplicaReplayWindow(fixture.Configuration.VoterIds, fixture.Options.ReplayLimits);
        var now = TimeProvider.System.GetUtcNow().ToUnixTimeMilliseconds();
        window.Admit(ReplicaSecurityFixture.VoterA, Guid.NewGuid(), now, now, ReplicaReplayPool.ReadBarrier);
        await Assert.That(window.TryAdmit(ReplicaSecurityFixture.VoterA, Guid.NewGuid(), now, now,
            ReplicaReplayPool.ReadBarrier, ReplicaRpc.ReadProbe, out var failure)).IsFalse();
        diagnostics.Report(failure);
        for (var index = 0; index < SuppressedCalls; index++)
        { diagnostics.Report(failure); }
        await Assert.That(capture.Entries.Count(item => item.Event == CapacityEvent)).IsEqualTo(1);
        diagnostics.Report(failure with { ObservedUnixMilliseconds = now + IntervalMilliseconds });
        var summary = capture.Entries.Last(item => item.Event == CapacityEvent);
        await Assert.That(summary.Number(Suppressed)).IsEqualTo((long)SuppressedCalls);
        await Assert.That(capture.Entries.Count(item => item.Event == CapacityEvent)).IsEqualTo(2);
    }
}

internal sealed class ReplicaReplayLogCapture : ILoggerProvider
{
    internal ConcurrentQueue<ReplicaReplayLogEntry> Entries { get; } = new();
    public ILogger CreateLogger(string categoryName) => new CaptureLogger(Entries);
    public void Dispose() { }

    private sealed class CaptureLogger(ConcurrentQueue<ReplicaReplayLogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var fields = ((IEnumerable<KeyValuePair<string, object?>>)(object)state!).ToDictionary(
                item => item.Key, item => item.Value, StringComparer.Ordinal);
            entries.Enqueue(new(eventId.Id, fields, formatter(state, exception)));
        }
    }
}

internal sealed record ReplicaReplayLogEntry(int Event, Dictionary<string, object?> Fields, string Rendered)
{
    internal long Number(string name) => Convert.ToInt64(Fields[name], CultureInfo.InvariantCulture);
}
