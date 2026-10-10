using System.Text.Json;
using KeyLoad.CrashHost;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleChunkSnapshotArtifactTests
{
    private const string SnapshotName = "chunk-artifact.json";
    private const char OversizeByte = 'A';

    [Test]
    public async Task AcChunk012OversizeArtifactRefusesBeforePublicationThenFullSnapshotAndCanonicalMergeRemainHealthy()
    {
        using var fixture = new SampleChunkCanonicalFixture();
        fixture.Open();
        var appendId = Guid.NewGuid();
        var append = new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            SampleChunkCanonicalFixture.Initial, SampleChunkCanonicalFixture.Tags);
        var original = fixture.Commit(appendId, append);
        var receipt = original.Get<CommitReceipt>();
        var snapshot = Snapshot(fixture, receipt);
        var path = Path.Combine(fixture.Owner.Directory, SnapshotName);
        var maximumBytes = fixture.Owner.Database.Limits.MaxBatchBytes;
        var before = fixture.Image();
        var position = fixture.Owner.Store.Position;
        var oversized = snapshot with { RawImage = new string(OversizeByte, maximumBytes) };
        var token = TestContext.Current!.Execution.CancellationToken;
        await Assert.That(async () => await SampleChunkSnapshotFile.WriteAsync(path, oversized, maximumBytes, token))
            .Throws<InvalidOperationException>();
        await Assert.That(File.Exists(path)).IsFalse();
        await Assert.That(fixture.Image()).IsEqualTo(before);
        await Assert.That(fixture.Owner.Store.Position).IsEqualTo(position);
        await RequireFileAsync(path, snapshot, maximumBytes, token);
        await SampleChunkCanonicalAssertions.Replay(fixture, appendId, append, original);
        fixture.Commit(new SealSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            fixture.WindowId, SampleChunkCanonicalFixture.AppendedRevision));
        fixture.Commit(new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            [SampleChunkCanonicalFixture.Late], SampleChunkCanonicalFixture.Tags));
        fixture.Commit(new MergeSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            fixture.WindowId, SampleChunkCanonicalFixture.CorrectedRevision));
        await SampleChunkCanonicalAssertions.Literal(fixture, SampleChunkCanonicalFixture.MergedRevision,
            SampleChunkCanonicalFixture.MergedGeneration, SampleChunkCanonicalFixture.CorrectedSequence, late: true);
        await RequireFileAsync(path, Snapshot(fixture, receipt), maximumBytes, token);
        await SampleChunkCanonicalAssertions.Replay(fixture, appendId, append, original);
    }

    private static SampleChunkCrashSnapshot Snapshot(SampleChunkCanonicalFixture fixture, CommitReceipt receipt)
        => new(fixture.Read(), fixture.Owner.Database.ReadSamples(SampleChunkCanonicalFixture.Principal,
            fixture.Owner.Partition, SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            SampleChunkCanonicalFixture.Start, SampleChunkCanonicalFixture.Until, SampleChunkCanonicalFixture.OutputLimit),
            fixture.Raw(), fixture.Owner.Store.Position, Convert.ToHexString(NativeSerialization.Serialize(receipt)), null);

    private static async Task RequireFileAsync(string path, SampleChunkCrashSnapshot expected,
        int maximumBytes, CancellationToken token)
    {
        await SampleChunkSnapshotFile.WriteAsync(path, expected, maximumBytes, token);
        var bytes = await SampleChunkSnapshotFile.ReadAsync(path, maximumBytes, token);
        var actual = JsonSerializer.Deserialize<SampleChunkCrashSnapshot>(bytes, JsonDefaults.Options);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected)));
    }
}
