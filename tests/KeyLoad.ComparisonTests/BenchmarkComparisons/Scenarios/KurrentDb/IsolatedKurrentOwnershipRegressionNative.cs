using KeyLoad.Comparisons.Targets;
using KurrentDB.Client;
using KurrentEventData = KurrentDB.Client.EventData;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedKurrentOwnershipRegressionNative
{
    internal const string ForeignSuffix = "ownership_foreign", PrivateSuffix = "ownership_private";
    private const string Payload = "{\"ownership\":\"native-different-id-conflict\"}";
    private const string Metadata = "{\"ownership\":\"independent-fixture\"}";

    internal sealed record Snapshot(Guid EventId, ulong Revision, byte[] Data, string Type, string ContentType, byte[] Metadata);

    internal static KurrentEventData CreateEvent()
        => new(Uuid.FromGuid(Guid.NewGuid()), KurrentConstants.EventType,
            System.Text.Encoding.UTF8.GetBytes(Payload), System.Text.Encoding.UTF8.GetBytes(Metadata), KurrentConstants.EventJson);

    internal static async Task<Snapshot> ReadOriginalAsync(KurrentDBClient reader, string stream, CancellationToken token)
    {
        var result = reader.ReadStreamAsync(Direction.Forwards, stream, StreamPosition.Start,
            maxCount: KurrentConstants.ReadLimit, cancellationToken: token);
        await Assert.That(await result.ReadState).IsEqualTo(ReadState.Ok);
        var items = new List<ResolvedEvent>(KurrentConstants.ReadLimit);
        await foreach (var item in result.WithCancellation(token))
        {
            items.Add(item);
        }
        await Assert.That(items.Count).IsEqualTo(KurrentConstants.ExpectedSingleEventCount);
        var resolved = items.Single();
        var original = resolved.OriginalEvent;
        var revision = resolved.OriginalEventNumber.ToUInt64();
        await Assert.That(revision).IsEqualTo(KurrentConstants.NativeFirstRevision);
        return new(original.EventId.ToGuid(), revision, original.Data.ToArray(), original.EventType,
            original.ContentType, original.Metadata.ToArray());
    }

    internal static async Task RequireUnchangedAsync(Snapshot before, Snapshot after)
    {
        await Assert.That(after.EventId).IsEqualTo(before.EventId);
        await Assert.That(after.Revision).IsEqualTo(before.Revision);
        await Assert.That(after.Type).IsEqualTo(before.Type);
        await Assert.That(after.ContentType).IsEqualTo(before.ContentType);
        await Assert.That(after.Data.AsSpan().SequenceEqual(before.Data)).IsTrue();
        await Assert.That(after.Metadata.AsSpan().SequenceEqual(before.Metadata)).IsTrue();
    }
}
