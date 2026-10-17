using KeyLoad.Comparisons.Targets;
using KurrentDB.Client;
using KurrentEventData = KurrentDB.Client.EventData;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedKurrentCleanupRegressionNative
{
    internal const int PrivateCount = 3;
    internal const string PrivatePrefix = "cleanup-private-", ForeignPrefix = "cleanup-foreign-";
    private const string Connection = "esdb://localhost:2113?tls=false";
    private const string Payload = "{\"cleanup\":\"native-foreign-preservation\"}";

    internal static KurrentDBClient CreateClient(Uri endpoint)
        => new(KurrentNativeSettings.CreateDirectNode(Connection, endpoint));

    internal static KurrentEventData CreateEvent()
        => new(Uuid.FromGuid(Guid.NewGuid()), KurrentConstants.EventType,
            System.Text.Encoding.UTF8.GetBytes(Payload), contentType: KurrentConstants.EventJson);

    internal static async Task RequireDeletedAsync(KurrentDBClient reader, IEnumerable<string> streams, CancellationToken token)
    {
        foreach (var stream in streams)
        {
            var result = reader.ReadStreamAsync(Direction.Forwards, stream, StreamPosition.Start,
                maxCount: KurrentConstants.ReadLimit, cancellationToken: token);
            await using var enumerator = result.GetAsyncEnumerator(token);
            await Assert.That(await result.ReadState).IsEqualTo(ReadState.StreamNotFound);
        }
    }

    internal static async Task RequireCompleteAsync(KurrentCleanupDiagnostic diagnostic, int count)
    {
        await Assert.That(diagnostic.Outcome).IsEqualTo(KurrentCleanupOutcome.Succeeded);
        await Assert.That(diagnostic.Counts.Tracked).IsEqualTo(count);
        await Assert.That(diagnostic.Counts.IsComplete).IsTrue();
        await Assert.That(diagnostic.Counts.PeakConcurrency <= NativeExecutionPolicyFixture.Lifecycle().Value.KurrentCleanupConcurrency).IsTrue();
        await Assert.That(diagnostic.LaterDisposalFailures).IsEqualTo(0);
    }
}
