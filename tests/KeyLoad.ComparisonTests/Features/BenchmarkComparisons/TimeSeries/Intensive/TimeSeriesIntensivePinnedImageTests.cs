using System.Text.RegularExpressions;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

/// <summary>AC-TSI-003: actual pinned image facts, independent of database replication qualification.</summary>
internal sealed class TimeSeriesIntensivePinnedImageTests
{
    [Test]
    public async Task AcTsi003PinnedNativeImageHasObservedPg18BootstrapPrimitives()
    {
        var facts = await TimeSeriesIntensivePinnedImageQualification.RunAsync(
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(facts.Context.SourceRevision).IsEqualTo(
            Environment.GetEnvironmentVariable(TimeSeriesIntensivePinnedImageProtocol.ShaEnvironment));
        await Assert.That(facts.Context.Repository).IsEqualTo(TimeSeriesIntensivePinnedImageProtocol.Repository);
        await Assert.That(facts.Context.Ref).IsEqualTo(TimeSeriesIntensivePinnedImageProtocol.MainRef);
        await Assert.That(facts.Image.Reference).IsEqualTo(TimeSeriesIntensivePinnedImageProtocol.Image);
        await Assert.That(Regex.IsMatch(facts.Image.ConfigId, TimeSeriesIntensivePinnedImageProtocol.ConfigPattern,
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(TimeSeriesIntensivePinnedImageProtocol.RegexSeconds))).IsTrue();
        await Assert.That(facts.Image.RepoDigests.Any(digest =>
            TimeSeriesIntensivePinnedImageProtocol.AcceptedRepoDigests.Contains(digest, StringComparer.Ordinal))).IsTrue();
        await Assert.That(facts.Image.Os).IsEqualTo(TimeSeriesIntensivePinnedImageProtocol.Linux);
        await Assert.That(facts.Observations[TimeSeriesIntensivePinnedImageProtocol.PgMajor])
            .IsEqualTo(TimeSeriesIntensivePinnedImageProtocol.PostgresMajor);
        await Assert.That(facts.Observations[TimeSeriesIntensivePinnedImageFields.ProbeVersion].StartsWith(
            TimeSeriesIntensivePinnedImageProtocol.Postgres18VersionPrefix, StringComparison.Ordinal)).IsTrue();
        await Assert.That(facts.Observations[TimeSeriesIntensivePinnedImageProtocol.PgData])
            .IsEqualTo(TimeSeriesIntensivePinnedImageProtocol.ProposedPgData);
        await Assert.That(facts.Observations[TimeSeriesIntensivePinnedImageProtocol.RootUid])
            .IsEqualTo(TimeSeriesIntensivePinnedImageProtocol.RootUser);
        await Assert.That(facts.Observations[TimeSeriesIntensivePinnedImageProtocol.PostgresUid])
            .IsNotEqualTo(TimeSeriesIntensivePinnedImageProtocol.RootUser);
        await Assert.That(facts.Observations[TimeSeriesIntensivePinnedImageProtocol.WriteVerified])
            .IsEqualTo(TimeSeriesIntensivePinnedImageProtocol.True);
        await Assert.That(facts.CleanupCompleted).IsTrue();
        await Assert.That(facts.Commands.All(command => command.ExitCode == 0)).IsTrue();
    }
}
