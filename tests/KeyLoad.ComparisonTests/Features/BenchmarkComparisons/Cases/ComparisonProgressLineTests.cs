namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-BC-LIVE-002: progress accepts only its closed privacy-preserving schema.</summary>
internal sealed class ComparisonProgressLineTests
{
    private const string Marker = "KeyLoadBenchmarkProgress phase=measure repetition=1 completed=7 total=10 failed=2 elapsedSeconds=30.125";

    [Test]
    [Arguments("oracle")]
    [Arguments("initialize")]
    [Arguments("warmup")]
    [Arguments("prepare")]
    [Arguments("measure")]
    [Arguments("validate")]
    [Arguments("complete")]
    public async Task AcceptsEveryClosedPhase(string phase)
        => await Assert.That(ComparisonProgressLine.IsValid(Marker.Replace("phase=measure", "phase=" + phase,
            StringComparison.Ordinal))).IsTrue();

    [Test]
    public async Task AcceptsNumericBoundsAndUnavailableTotals()
    {
        await Assert.That(ComparisonProgressLine.IsValid(
            "KeyLoadBenchmarkProgress phase=initialize repetition=0 completed=0 total=0 failed=0 elapsedSeconds=0")).IsTrue();
        await Assert.That(ComparisonProgressLine.IsValid(
            "KeyLoadBenchmarkProgress phase=measure repetition=2147483647 completed=2147483647 total=2147483647 failed=2147483647 elapsedSeconds=2147483647.125")).IsTrue();
    }

    [Test]
    [Arguments("phase=measure", "phase=secret")]
    [Arguments("completed=7", "completed=-1")]
    [Arguments("completed=7", "completed=2147483648")]
    [Arguments("completed=7", "completed=11")]
    [Arguments("failed=2", "failed=8")]
    [Arguments("repetition=1", "repetition=+1")]
    [Arguments("elapsedSeconds=30.125", "elapsedSeconds=NaN")]
    [Arguments("elapsedSeconds=30.125", "elapsedSeconds=Infinity")]
    [Arguments("elapsedSeconds=30.125", "elapsedSeconds=-1")]
    [Arguments("elapsedSeconds=30.125", "elapsedSeconds=1e9")]
    [Arguments("elapsedSeconds=30.125", "elapsedSeconds=30,125")]
    public async Task RejectsInvalidCountsPhasesAndElapsed(string original, string replacement)
        => await Assert.That(ComparisonProgressLine.IsValid(Marker.Replace(original, replacement,
            StringComparison.Ordinal))).IsFalse();

    [Test]
    public async Task RejectsUntrustedTextPrefixesNewlinesAndOversize()
    {
        foreach (var invalid in new[] { "", "container " + Marker, Marker + " endpoint=private", Marker + "\n", Marker + "\r",
            Marker + new string('x', ComparisonProgressLine.MaximumCharacters), Marker.Replace("30.125", new string('9', 400), StringComparison.Ordinal) })
        {
            await Assert.That(ComparisonProgressLine.IsValid(invalid)).IsFalse();
        }
    }

    [Test]
    public async Task ExtractsOnlyAValidMarkerFromThePinnedNativeUtcTimestamp()
    {
        const string timestamp = "2026-10-04T15:20:43.3755008Z ";
        await Assert.That(ComparisonProgressLine.TryFromNativeLog(timestamp + Marker, out var native)).IsTrue();
        await Assert.That(native).IsEqualTo(Marker);
        await Assert.That(ComparisonProgressLine.TryFromNativeLog(Marker, out var raw)).IsTrue();
        await Assert.That(raw).IsEqualTo(Marker);
        foreach (var invalid in new[] { "private " + Marker, timestamp + " " + Marker, timestamp + Marker + " private=payload",
            timestamp.Replace("10-04", "02-30", StringComparison.Ordinal) + Marker, timestamp[..^1] + Marker })
        {
            await Assert.That(ComparisonProgressLine.TryFromNativeLog(invalid, out var rejected)).IsFalse();
            await Assert.That(rejected).IsEmpty();
        }
    }

    [Test]
    public async Task DerivesOnlyTheValidatedCellSiblingFailurePath()
    {
        var root = Path.Combine(Path.GetTempPath(), "comparison-progress-path");
        var evidence = Path.Combine(root, "workers", "keyload-n3-vector-exact");
        await Assert.That(ComparisonProgressLine.PathForEvidenceDirectory(evidence)).IsEqualTo(
            Path.Combine(root, "failures", "keyload-n3-vector-exact", ComparisonProgressLine.FileName));
        foreach (var invalid in new[] { Path.Combine(root, "reports", "cell"), Path.Combine(root, "workers", "CELL"),
            Path.Combine(root, "workers", "cell.."), Path.Combine(root, "workers", new string('x', 129)), "workers/cell" })
        {
            await Assert.That(() => ComparisonProgressLine.PathForEvidenceDirectory(invalid)).Throws<ArgumentException>();
        }
    }
}
