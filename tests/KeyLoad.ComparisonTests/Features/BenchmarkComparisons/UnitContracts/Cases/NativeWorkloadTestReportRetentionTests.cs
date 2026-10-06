using KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class NativeWorkloadTestReportRetentionTests
{
    [Test]
    public async Task OriginalReportBytesRemainSeparateFromMeasurementArchivesAndCannotBeOverwritten()
    {
        using var directory = new ImageBundleTestDirectory();
        var output = Path.Combine(directory.Root, "output");
        var reports = Path.Combine(output, "TestResults");
        var evidence = Path.Combine(directory.Root, "isolated", "workers", "actual-cell");
        Directory.CreateDirectory(reports);
        var original = Path.Combine(reports, "original.trx");
        await File.WriteAllTextAsync(original, "original-report-byte-canary");
        NativeWorkloadTestReports.Retain(output, evidence);
        var retained = Path.Combine(directory.Root, "isolated", "failures", "actual-cell", "TestResults", "original.trx");
        await Assert.That(await File.ReadAllBytesAsync(retained)).IsEquivalentTo(await File.ReadAllBytesAsync(original));
        await Assert.That(Directory.Exists(evidence)).IsFalse();
        await Assert.That(() => NativeWorkloadTestReports.Retain(output, evidence)).Throws<IOException>();
        await File.WriteAllTextAsync(Path.Combine(reports, "second.trx"), "second-report-byte-canary");
        await Assert.That(() => NativeWorkloadTestReports.Retain(output, evidence)).Throws<InvalidDataException>();
    }
}
