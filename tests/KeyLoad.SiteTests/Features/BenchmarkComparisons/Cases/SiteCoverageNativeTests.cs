using System.Text;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteCoverageNativeTests
{
    [Test]
    public async Task AC_BC_024_RealNodePartitionsUnionWithinSnapshotsAndRetainPhysicalFunctions()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = new SiteCoverageFixtureDirectory();
        await temporary.CreateAsync(cancellationToken);
        var source = await temporary.WriteSourceAsync(SiteCoverageNativeTestSupport.FixtureRelativePath,
            SiteCoverageNativeTestSupport.FixtureSource, cancellationToken);
        var trueReceipt = await SiteCoverageNativeTestSupport.RunNodeCoverageAsync(temporary,
            SiteCoverageNativeTestSupport.TrueArgument, cancellationToken);
        var falseReceipt = await SiteCoverageNativeTestSupport.RunNodeCoverageAsync(temporary,
            SiteCoverageNativeTestSupport.FalseArgument, cancellationToken);
        await SiteCoverageNativeEvidence.CaptureAsync(temporary, SiteCoverageNativeTestSupport.FixtureRelativePath,
            trueReceipt, cancellationToken);
        await SiteCoverageNativeEvidence.CaptureAsync(temporary, SiteCoverageNativeTestSupport.FixtureRelativePath,
            falseReceipt, cancellationToken);
        await Assert.That(trueReceipt.StandardOutput.Trim()).IsEqualTo(SiteCoverageTokens.TrueResultOutput);
        await Assert.That(falseReceipt.StandardOutput.Trim()).IsEqualTo(SiteCoverageTokens.FalseResultOutput);
        var sources = new Dictionary<string, SiteCoverageSourceEntry>(StringComparer.Ordinal)
        {
            [SiteCoverageNativeTestSupport.FixtureRelativePath] = new(SiteCoverageNativeTestSupport.FixtureRelativePath,
                SiteCoverageSourceManifestWriter.Hash(source), SiteCoverageNativeTestSupport.FixtureSource.Length),
        };
        var trueSnapshots = SiteCoverageNativeTestSupport.ParseSnapshots(trueReceipt, temporary.Root, sources);
        var falseSnapshots = SiteCoverageNativeTestSupport.ParseSnapshots(falseReceipt, temporary.Root, sources);
        await SiteCoverageNativeTestSupport.AssertSnapshotIdentity(trueReceipt, trueSnapshots, temporary.Root);
        await SiteCoverageNativeTestSupport.AssertSnapshotIdentity(falseReceipt, falseSnapshots, temporary.Root);
        await SiteCoverageNativeFixtureAssertions.AssertPhysicalFunctionsAreDistinct(trueReceipt, temporary.Root);
        await SiteCoverageNativeFixtureAssertions.AssertRealNodeOmittedBranchInference(trueReceipt, falseReceipt,
            temporary.Root);

        var joined = trueSnapshots.Concat(falseSnapshots).ToArray();
        var sourceEntry = sources[SiteCoverageNativeTestSupport.FixtureRelativePath];
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
        await SiteCoverageNativeFixtureAssertions.AssertMalformedRangesRejected(trueReceipt, temporary.Root, sources);
        await SiteCoverageNativeFixtureAssertions.AssertCrossingRangesRejected(trueReceipt, temporary.Root, sources);
    }

    [Test]
    public async Task AC_BC_024_RealNodeEqualSpanUsesLastNativeFunctionWithinSnapshot()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = new SiteCoverageFixtureDirectory();
        await temporary.CreateAsync(cancellationToken);
        var relativePath = SiteCoverageNativeTestSupport.FixtureRelativePath;
        var source = await temporary.WriteSourceAsync(relativePath,
            SiteCoverageNativeTestSupport.EqualSpanFixtureSource, cancellationToken);
        await Assert.That(SiteCoverageNativeTestSupport.EqualSpanFixtureSource.EndsWith('\n') ||
            SiteCoverageNativeTestSupport.EqualSpanFixtureSource.EndsWith('\r')).IsFalse();
        var receipt = await SiteCoverageNativeTestSupport.RunNodeCoverageForAsync(temporary,
            SiteCoverageNativeTestSupport.EqualSpanFunctionName, relativePath, cancellationToken);
        await SiteCoverageNativeEvidence.CaptureAsync(temporary, relativePath, receipt, cancellationToken);
        var sources = new Dictionary<string, SiteCoverageSourceEntry>(StringComparer.Ordinal)
        {
            [relativePath] = new(relativePath, SiteCoverageSourceManifestWriter.Hash(source), source.Length),
        };
        var snapshots = SiteCoverageNativeTestSupport.ParseSnapshots(receipt, temporary.Root, sources);
        var snapshot = snapshots.Single();
        var functions = snapshot.Functions.ToList();
        var wrapperIndex = functions.FindIndex(function => function.FunctionName.Length == SiteCoverageTokens.Zero);
        var uncalledIndex = functions.FindIndex(function =>
            function.FunctionName == SiteCoverageNativeTestSupport.EqualSpanFunctionName);
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
        var original = await temporary.WriteSourceAsync(SiteCoverageNativeTestSupport.FixtureRelativePath,
            SiteCoverageNativeTestSupport.FixtureSource, cancellationToken);
        var baseline = new SiteCoverageSourceManifest(SiteCoverageTokens.Schema,
            new string('a', SiteCoverageTokens.RevisionLength), SiteCoverageTokens.NodeVersionPrefix + SiteCoverageTokens.One,
            [new(SiteCoverageNativeTestSupport.FixtureRelativePath, SiteCoverageSourceManifestWriter.Hash(original),
                SiteCoverageNativeTestSupport.FixtureSource.Length)]);
        var path = Path.Combine(temporary.Root, SiteCoverageNativeTestSupport.FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar));
        await File.WriteAllTextAsync(path, SiteCoverageNativeTestSupport.FixtureSource + SiteCoverageTokens.Newline,
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
    public async Task AC_BC_024_MissingNativeRangesLeaveEveryExecutableLineUncovered()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = new SiteCoverageFixtureDirectory();
        await temporary.CreateAsync(cancellationToken);
        var source = await temporary.WriteSourceAsync(SiteCoverageNativeTestSupport.FixtureRelativePath,
            SiteCoverageNativeTestSupport.FixtureSource, cancellationToken);
        var entry = new SiteCoverageSourceEntry(SiteCoverageNativeTestSupport.FixtureRelativePath,
            SiteCoverageSourceManifestWriter.Hash(source), SiteCoverageNativeTestSupport.FixtureSource.Length);
        var metrics = SiteCoverageAnalyzer.Analyze(entry, temporary.Root, []);
        await Assert.That(metrics.ExecutableLines).IsEqualTo(SiteCoverageTokens.Nine);
        await Assert.That(metrics.CoveredLines).IsEqualTo(SiteCoverageTokens.Zero);
        await Assert.That(metrics.BlockOutcomes).IsEqualTo(SiteCoverageTokens.Zero);
    }
}
