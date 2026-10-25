using System.Text;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal sealed class SiteCoverageTests
{
    [Test]
    public async Task AC_BC_024_RealNodePartitionsUnionWithinSnapshotsAndRetainPhysicalFunctions()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = new SiteCoverageFixtureDirectory();
        await temporary.CreateAsync(cancellationToken);
        var source = await temporary.WriteSourceAsync(SiteCoverageTestSupport.FixtureRelativePath,
            SiteCoverageTestSupport.FixtureSource, cancellationToken);
        var trueReceipt = await SiteCoverageTestSupport.RunNodeCoverageAsync(temporary,
            SiteCoverageTestSupport.TrueArgument, cancellationToken);
        var falseReceipt = await SiteCoverageTestSupport.RunNodeCoverageAsync(temporary,
            SiteCoverageTestSupport.FalseArgument, cancellationToken);
        await SiteCoverageEvidence.CaptureAsync(temporary, SiteCoverageTestSupport.FixtureRelativePath,
            trueReceipt, cancellationToken);
        await SiteCoverageEvidence.CaptureAsync(temporary, SiteCoverageTestSupport.FixtureRelativePath,
            falseReceipt, cancellationToken);
        await Assert.That(trueReceipt.StandardOutput.Trim()).IsEqualTo(SiteCoverageTokens.TrueResultOutput);
        await Assert.That(falseReceipt.StandardOutput.Trim()).IsEqualTo(SiteCoverageTokens.FalseResultOutput);
        var sources = new Dictionary<string, SiteCoverageSourceEntry>(StringComparer.Ordinal)
        {
            [SiteCoverageTestSupport.FixtureRelativePath] = new(SiteCoverageTestSupport.FixtureRelativePath,
                SiteCoverageSourceManifestWriter.Hash(source), SiteCoverageTestSupport.FixtureSource.Length),
        };
        var trueSnapshots = SiteCoverageTestSupport.ParseSnapshots(trueReceipt, temporary.Root, sources);
        var falseSnapshots = SiteCoverageTestSupport.ParseSnapshots(falseReceipt, temporary.Root, sources);
        await SiteCoverageTestSupport.AssertSnapshotIdentity(trueReceipt, trueSnapshots, temporary.Root);
        await SiteCoverageTestSupport.AssertSnapshotIdentity(falseReceipt, falseSnapshots, temporary.Root);
        await SiteCoverageFixtureAssertions.AssertPhysicalFunctionsAreDistinct(trueReceipt, temporary.Root);
        await SiteCoverageFixtureAssertions.AssertRealNodeOmittedBranchInference(trueReceipt, falseReceipt,
            temporary.Root);

        var joined = trueSnapshots.Concat(falseSnapshots).ToArray();
        var sourceEntry = sources[SiteCoverageTestSupport.FixtureRelativePath];
        var metrics = SiteCoverageAnalyzer.Analyze(sourceEntry, temporary.Root, joined);
        var lines = SiteCoverageAnalyzer.AnalyzeLines(sourceEntry, temporary.Root, joined);
        await Assert.That(metrics.ExecutableLines).IsEqualTo(SiteCoverageTokens.Nine);
        await Assert.That(metrics.CoveredLines).IsEqualTo(SiteCoverageTokens.Eight);
        await Assert.That(lines.Covered.Contains(SiteCoverageTokens.Four)).IsTrue();
        await Assert.That(lines.Covered.Contains(SiteCoverageTokens.Six)).IsTrue();
        await Assert.That(lines.Covered.Contains(SiteCoverageTokens.Eight)).IsFalse();
        await Assert.That(metrics.BlockOutcomes).IsEqualTo(SiteCoverageTokens.Two);
        await Assert.That(metrics.CoveredBlockOutcomes).IsEqualTo(SiteCoverageTokens.Two);

        var reversed = falseSnapshots.Concat(trueSnapshots).ToArray();
        var reversedMetrics = SiteCoverageAnalyzer.Analyze(sourceEntry, temporary.Root, reversed);
        await Assert.That(reversedMetrics).IsEqualTo(metrics);
        await SiteCoverageFixtureAssertions.AssertMalformedRangesRejected(trueReceipt, temporary.Root, sources);
        await SiteCoverageFixtureAssertions.AssertCrossingRangesRejected(trueReceipt, temporary.Root, sources);
    }

    [Test]
    public async Task AC_BC_024_RealNodeEqualSpanUsesLastFunctionWithinSnapshot()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = new SiteCoverageFixtureDirectory();
        await temporary.CreateAsync(cancellationToken);
        var relativePath = SiteCoverageTestSupport.FixtureRelativePath;
        var source = await temporary.WriteSourceAsync(relativePath,
            SiteCoverageTestSupport.EqualSpanFixtureSource, cancellationToken);
        await Assert.That(SiteCoverageTestSupport.EqualSpanFixtureSource.EndsWith('\n') ||
            SiteCoverageTestSupport.EqualSpanFixtureSource.EndsWith('\r')).IsFalse();
        var receipt = await SiteCoverageTestSupport.RunNodeCoverageForAsync(temporary,
            SiteCoverageTestSupport.EqualSpanFunctionName, relativePath, cancellationToken);
        await SiteCoverageEvidence.CaptureAsync(temporary, relativePath, receipt, cancellationToken);
        var sources = new Dictionary<string, SiteCoverageSourceEntry>(StringComparer.Ordinal)
        {
            [relativePath] = new(relativePath, SiteCoverageSourceManifestWriter.Hash(source), source.Length),
        };
        var snapshots = SiteCoverageTestSupport.ParseSnapshots(receipt, temporary.Root, sources);
        var snapshot = snapshots.Single();
        var functions = snapshot.Functions.ToList();
        var wrapperIndex = functions.FindIndex(function => function.FunctionName.Length == SiteCoverageTokens.Zero);
        var uncalledIndex = functions.FindIndex(function =>
            function.FunctionName == SiteCoverageTestSupport.EqualSpanFunctionName);
        await Assert.That(wrapperIndex >= SiteCoverageTokens.Zero).IsTrue();
        await Assert.That(uncalledIndex > wrapperIndex).IsTrue();
        var wrapper = functions[wrapperIndex];
        var uncalled = functions[uncalledIndex];
        await Assert.That(uncalled.FunctionStart).IsEqualTo(wrapper.FunctionStart);
        await Assert.That(uncalled.FunctionEnd).IsEqualTo(wrapper.FunctionEnd);
        var sourceEntry = sources[relativePath];
        var metrics = SiteCoverageAnalyzer.Analyze(sourceEntry, temporary.Root, snapshots);
        var lines = SiteCoverageAnalyzer.AnalyzeLines(sourceEntry, temporary.Root, snapshots);
        await Assert.That(metrics.ExecutableLines).IsEqualTo(SiteCoverageTokens.One);
        await Assert.That(metrics.CoveredLines).IsEqualTo(SiteCoverageTokens.Zero);
        await Assert.That(lines.Covered.Count).IsEqualTo(SiteCoverageTokens.Zero);
    }

    [Test]
    public async Task AC_BC_024_SourceHashChangeAfterBaselineFailsClosed()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = new SiteCoverageFixtureDirectory();
        await temporary.CreateAsync(cancellationToken);
        var original = await temporary.WriteSourceAsync(SiteCoverageTestSupport.FixtureRelativePath,
            SiteCoverageTestSupport.FixtureSource, cancellationToken);
        var baseline = new SiteCoverageSourceManifest(SiteCoverageTokens.Schema,
            new string('a', SiteCoverageTokens.RevisionLength), SiteCoverageTokens.NodeVersionPrefix + SiteCoverageTokens.One,
            [new(SiteCoverageTestSupport.FixtureRelativePath, SiteCoverageSourceManifestWriter.Hash(original),
                SiteCoverageTestSupport.FixtureSource.Length)]);
        var path = Path.Combine(temporary.Root, SiteCoverageTestSupport.FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar));
        await File.WriteAllTextAsync(path, SiteCoverageTestSupport.FixtureSource + SiteCoverageTokens.Newline,
            new UTF8Encoding(false), cancellationToken);
        var rejected = false;
        try
        {
            await SiteCoverageSourceManifestWriter.VerifyUnchangedAsync(temporary.Root, baseline);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }

        await Assert.That(rejected).IsTrue();
    }

    [Test]
    public async Task AC_BC_024_MissingRangesLeaveEveryExecutableLineUncovered()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = new SiteCoverageFixtureDirectory();
        await temporary.CreateAsync(cancellationToken);
        var source = await temporary.WriteSourceAsync(SiteCoverageTestSupport.FixtureRelativePath,
            SiteCoverageTestSupport.FixtureSource, cancellationToken);
        var entry = new SiteCoverageSourceEntry(SiteCoverageTestSupport.FixtureRelativePath,
            SiteCoverageSourceManifestWriter.Hash(source), SiteCoverageTestSupport.FixtureSource.Length);
        var metrics = SiteCoverageAnalyzer.Analyze(entry, temporary.Root, []);
        await Assert.That(metrics.ExecutableLines).IsEqualTo(SiteCoverageTokens.Nine);
        await Assert.That(metrics.CoveredLines).IsEqualTo(SiteCoverageTokens.Zero);
        await Assert.That(metrics.BlockOutcomes).IsEqualTo(SiteCoverageTokens.Zero);
    }
}
