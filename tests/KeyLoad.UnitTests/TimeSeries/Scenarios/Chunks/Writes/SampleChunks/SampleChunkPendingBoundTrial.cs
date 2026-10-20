using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleChunkPendingBoundTrial(SampleChunkCanonicalFixture fixture)
{
    internal Guid SecondWindow { get; } = Guid.NewGuid();
    internal Guid ExcessWindow { get; } = Guid.NewGuid();
    private static DateTimeOffset SecondStart => SampleChunkCanonicalFixture.Until;
    private static DateTimeOffset SecondEnd => SecondStart.AddHours(SampleChunkPendingBoundProtocol.WindowHours);
    private static SampleData First => new(SampleChunkPendingBoundProtocol.FirstId,
        SampleChunkCanonicalFixture.Start, SampleChunkPendingBoundProtocol.FirstValue);
    private static SampleData Second => new(SampleChunkPendingBoundProtocol.SecondId, SecondStart,
        SampleChunkPendingBoundProtocol.SecondValue);
    private static SampleData FirstLate => new(SampleChunkPendingBoundProtocol.FirstLateId,
        SampleChunkCanonicalFixture.Start.AddTicks(SampleChunkPendingBoundProtocol.FirstSequence),
        SampleChunkPendingBoundProtocol.LateValue);
    private static SampleData SecondLate => new(SampleChunkPendingBoundProtocol.SecondLateId,
        SecondStart.AddTicks(SampleChunkPendingBoundProtocol.FirstSequence), SampleChunkPendingBoundProtocol.LateValue);
    internal static AppendSamples Overflow => Append(SecondLate);

    private const string AppendKind = "appendSamples";
    private const string OpenKind = "openSampleChunkWindow";
    private const string SealKind = "sealSampleChunkWindow";

    internal async Task SeedAsync(List<(Command<DatabaseCommand> Operation, Result<object> Result)> originals)
    {
        await CommitAsync(new OpenSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, SampleChunkCanonicalFixture.Start,
            SampleChunkCanonicalFixture.Until), OpenKind, SampleChunkCanonicalFixture.FirstRevision, originals);
        await CommitAsync(new OpenSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            SecondWindow, SecondStart, SecondEnd), OpenKind, SampleChunkCanonicalFixture.FirstRevision, originals);
        await CommitAsync(Append(First), AppendKind, SampleChunkPendingBoundProtocol.FirstSequence, originals);
        await CommitAsync(Append(Second), AppendKind, SampleChunkPendingBoundProtocol.SecondSequence, originals);
        await CommitAsync(new SealSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            fixture.WindowId, SampleChunkPendingBoundProtocol.Appended), SealKind, SampleChunkPendingBoundProtocol.Sealed, originals);
        await CommitAsync(new SealSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            SecondWindow, SampleChunkPendingBoundProtocol.Appended), SealKind, SampleChunkPendingBoundProtocol.Sealed, originals);
        await CommitAsync(Append(FirstLate), AppendKind, SampleChunkPendingBoundProtocol.ThirdSequence, originals);
    }

    private Task CommitAsync(Mutation mutation, string kind, long revision,
        List<(Command<DatabaseCommand> Operation, Result<object> Result)> originals)
        => SampleChunkCorrectionBoundReceipts.CommitAsync(fixture, mutation, kind, revision, originals);

    internal Task RequireAsync(bool firstMerged, bool secondCorrected)
        => RequireAsync(fixture.Owner.Database, fixture.Owner.Store, firstMerged, secondCorrected);

    internal async Task RequireAsync(DatabaseEngine database, ZoneTreeStore store, bool firstMerged, bool secondCorrected)
    {
        var image = SampleChunkCorrectionBoundColdAssertions.Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        var failures = new List<Exception>();
        var firstRows = new SampleRecord[] { Row(First, SampleChunkPendingBoundProtocol.FirstSequence),
            Row(FirstLate, SampleChunkPendingBoundProtocol.ThirdSequence) };
        var secondRows = secondCorrected
            ? new SampleRecord[] { Row(Second, SampleChunkPendingBoundProtocol.SecondSequence),
                Row(SecondLate, SampleChunkPendingBoundProtocol.FourthSequence) }
            : [Row(Second, SampleChunkPendingBoundProtocol.SecondSequence)];
        await ServerFailureObserver.ObserveAsync(() => WindowAsync(database, store, fixture.WindowId, SampleChunkCanonicalFixture.Start, SampleChunkCanonicalFixture.Until,
            firstMerged ? SampleChunkPendingBoundProtocol.MergedGeneration : SampleChunkPendingBoundProtocol.Generation,
            firstMerged ? SampleChunkPendingBoundProtocol.Merged : SampleChunkPendingBoundProtocol.Corrected,
            SampleChunkPendingBoundProtocol.ThirdSequence, firstRows), failures);
        await ServerFailureObserver.ObserveAsync(() => WindowAsync(database, store, SecondWindow, SecondStart, SecondEnd, SampleChunkPendingBoundProtocol.Generation,
            secondCorrected ? SampleChunkPendingBoundProtocol.Corrected : SampleChunkPendingBoundProtocol.Sealed,
            secondCorrected ? SampleChunkPendingBoundProtocol.FourthSequence : SampleChunkPendingBoundProtocol.SecondSequence,
            secondRows), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var raw = database.ReadSamples(SampleChunkCanonicalFixture.Principal, fixture.Owner.Partition,
                SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series, SampleChunkCanonicalFixture.Start,
                SecondEnd, SampleChunkCanonicalFixture.OutputLimit);
            await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(raw)))
                .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(firstRows.Concat(secondRows).ToArray())));
        }, failures);
        await ServerFailureObserver.ObserveAsync(() =>
            SampleChunkPendingWindowEnrollmentBound.AbsentAsync(store, fixture.Owner.Partition, ExcessWindow), failures);
        await ServerFailureObserver.ObserveAsync(() =>
            SampleChunkCorrectionBoundColdAssertions.UnchangedAsync(store, image, position, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task WindowAsync(DatabaseEngine database, ZoneTreeStore store, Guid id, DateTimeOffset from, DateTimeOffset until, long generation,
        long revision, long sequence, SampleRecord[] rows)
    {
        var request = fixture.Request with
        {
            WindowId = id
        };
        var actual = database.ReadSampleChunkWindow(SampleChunkCanonicalFixture.Principal, request);
        var expected = new SampleChunkWindowResult(id, from, until, generation, revision, sequence, null,
            [.. rows], store.Position);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected)));
    }

    private static AppendSamples Append(SampleData sample) => new(SampleChunkCanonicalFixture.Set,
        SampleChunkCanonicalFixture.Series, [sample], SampleChunkCanonicalFixture.Tags);
    private static SampleRecord Row(SampleData sample, long sequence) => new(SampleChunkCanonicalFixture.Series,
        sample, sequence, SampleChunkCanonicalFixture.Tags);
}
