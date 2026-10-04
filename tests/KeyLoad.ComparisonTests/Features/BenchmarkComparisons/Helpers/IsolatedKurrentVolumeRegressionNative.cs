using System.Reflection;
using System.Text;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using KurrentDB.Client;
using KurrentEventData = KurrentDB.Client.EventData;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedKurrentVolumeRegressionNative
{
    private const int Workers = 16;
    private const string Payload = "{\"fixture\":\"canonical-volume\"}";
    private const string TraceId = "0123456789abcdef0123456789abcdef";
    private const string SpanId = "0123456789abcdef";
    private const string Metadata = $"{{\"owner\":\"canonical-volume\",\"$traceId\":\"{TraceId}\",\"$spanId\":\"{SpanId}\"}}";

    internal static async Task RequireCanonicalProfileAsync(ComparisonOptions options)
    {
        await Assert.That(options.Documents).IsEqualTo(4_096);
        await Assert.That(options.Repetitions).IsEqualTo(5);
        await Assert.That(options.Warmup).IsEqualTo(256);
        await Assert.That(options.Operations).IsEqualTo(10_000);
        await Assert.That(options.Concurrency).IsEqualTo(16);
        var count = checked(options.Documents + options.Repetitions * checked(options.Warmup + options.Operations)
            + 2);
        await Assert.That(ReadOwnershipProbeCount()).IsEqualTo(2);
        await Assert.That(count).IsEqualTo(55_378);
    }

    internal static int ReadOwnershipProbeCount()
    {
        var value = typeof(KurrentConstants).GetField(nameof(KurrentConstants.OwnershipProbeCount),
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetRawConstantValue();
        return value is int count ? count : throw new InvalidOperationException("KurrentOwnershipProbeCountMetadataInvalid");
    }

    internal static KurrentEventData CreateEvent()
        => new(Uuid.FromGuid(Guid.NewGuid()), KurrentConstants.EventType, Encoding.UTF8.GetBytes(Payload),
            Encoding.UTF8.GetBytes(Metadata), KurrentConstants.EventJson);

    internal static KurrentEventData CreateForeignEvent()
        => new(Uuid.FromGuid(Guid.NewGuid()), KurrentConstants.EventType,
            Encoding.UTF8.GetBytes("{\"fixture\":\"independent-foreign-original\"}"),
            Encoding.UTF8.GetBytes("{\"owner\":\"independent\"}"), KurrentConstants.EventJson);

    internal static async Task RequireDeletedAsync(KurrentDBClient reader, string stream, CancellationToken token)
    {
        var result = reader.ReadStreamAsync(Direction.Forwards, stream, StreamPosition.Start,
            maxCount: KurrentConstants.ReadLimit, cancellationToken: token);
        await using var enumerator = result.GetAsyncEnumerator(token);
        await Assert.That(await result.ReadState).IsEqualTo(ReadState.StreamNotFound);
    }

    internal static async Task RequireCompleteAsync(KurrentCleanupDiagnostic diagnostic, int count)
    {
        var counts = diagnostic.Counts;
        await Assert.That(diagnostic.Outcome).IsEqualTo(KurrentCleanupOutcome.Succeeded);
        await Assert.That(diagnostic.Stage).IsEqualTo(KurrentCleanupStage.Complete);
        await Assert.That(diagnostic.Reason).IsEqualTo(KurrentCleanupFailureReason.None);
        await Assert.That(diagnostic.GrpcStatus).IsNull();
        await Assert.That(counts).IsEqualTo(new KurrentCleanupCounts(count, count, count, 0, 0, counts.PeakConcurrency));
        await Assert.That(counts.IsComplete).IsTrue();
        await Assert.That(counts.PeakConcurrency <= Workers).IsTrue();
        await Assert.That(diagnostic.CancellationRequested).IsFalse();
        await Assert.That(diagnostic.DeadlineExpired).IsFalse();
        await Assert.That(diagnostic.ElapsedMilliseconds <= KurrentConstants.CleanupHostTimeoutSeconds * 1_000L).IsTrue();
        await Assert.That(diagnostic.LaterDisposalFailures).IsEqualTo(0);
    }

}
