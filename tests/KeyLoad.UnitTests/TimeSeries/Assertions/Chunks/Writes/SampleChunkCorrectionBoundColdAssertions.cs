using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleChunkCorrectionBoundColdAssertions
{
    private const int ResultLimit = 259;
    private const long Generation = 2;
    private const int ImageLimit = 4096;
    private const string ImageBound = "The correction-cap whole image exceeds its existing fixture bound.";

    internal static string Image(ZoneTreeStore store) => store.Read(view =>
    {
        var scan = view.Scan([], ImageLimit);
        if (scan.HasMore)
        {
            throw new InvalidOperationException(ImageBound);
        }
        return Convert.ToHexString(JsonDefaults.Serialize(scan.Records.Select(record =>
            new[] { Convert.ToHexString(record.Key.Span), Convert.ToHexString(record.Value.Span) }).ToArray()));
    });

    internal static async Task UnchangedAsync(ZoneTreeStore store, string image, long position, byte[] identity)
    {
        await Assert.That(Image(store)).IsEqualTo(image);
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(JsonDefaults.Serialize(store.Identity).SequenceEqual(identity)).IsTrue();
    }

    internal static async Task RowsAsync(DatabaseEngine database, ZoneTreeStore store,
        SampleChunkCanonicalFixture fixture, long revision, ImmutableArray<SampleRecord> rows)
    {
        var image = Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var request = fixture.Request with
            {
                Limit = ResultLimit
            };
            var expected = new SampleChunkWindowResult(fixture.WindowId, SampleChunkCanonicalFixture.Start,
                SampleChunkCanonicalFixture.Until, Generation, revision, rows.Length, null, rows, store.Position);
            var actual = database.ReadSampleChunkWindow(SampleChunkCanonicalFixture.Principal, request);
            await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
            var raw = database.ReadSamples(SampleChunkCanonicalFixture.Principal, fixture.Owner.Partition,
                SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
                SampleChunkCanonicalFixture.Start, SampleChunkCanonicalFixture.Until, ResultLimit);
            await Assert.That(JsonDefaults.Serialize(raw).SequenceEqual(JsonDefaults.Serialize(rows))).IsTrue();
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => UnchangedAsync(store, image, position, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store,
        Command<DatabaseCommand> operation, Result<object> original)
    {
        var image = Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var actual = database.ApplyEmbedded(operation, cancellationToken: default);
            await Assert.That(SampleRollupWholeFlow.Outcome(actual)).IsEqualTo(SampleRollupWholeFlow.Outcome(original));
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => UnchangedAsync(store, image, position, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
