namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-006/007: genuine Node/file validation of portable image evidence; no Docker proof is inferred.</summary>
internal sealed class ImageBundleFileTests
{
    [Test]
    [Arguments("valid")]
    [Arguments("archive-hash")]
    [Arguments("archive-length")]
    [Arguments("archive-name")]
    [Arguments("archive-empty")]
    [Arguments("archive-large")]
    [Arguments("archive-symlink")]
    [Arguments("archive-directory")]
    [Arguments("ancestor-symlink")]
    [Arguments("missing")]
    [Arguments("duplicate-bundle")]
    [Arguments("duplicate-receipt")]
    [Arguments("duplicate-manifest")]
    [Arguments("extra-field")]
    [Arguments("extra-image")]
    [Arguments("source")]
    [Arguments("run")]
    [Arguments("attempt")]
    [Arguments("repository")]
    [Arguments("ref")]
    [Arguments("base")]
    [Arguments("config")]
    [Arguments("manifest-bytes")]
    [Arguments("manifest-digest")]
    [Arguments("reference")]
    [Arguments("revision-label")]
    public async Task AcIso006BundleValidationRejectsChangedOrUnsafeEvidence(string corruption)
    {
        using var directory = new ImageBundleTestDirectory();
        var result = await IsolatedAggregateNodeProcess.RunAsync(
            ["--input-type=module", "-e", ImageBundleNodeProgram.Source,
                IsolatedAggregateNodeProcess.Module("image-bundle-read.mjs"), directory.Root, corruption],
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(corruption == "valid" ? 0 : 1);
        await Assert.That(result.Error).IsEmpty();
        await Assert.That(result.Output.Trim()).IsEqualTo(corruption == "valid" ? "accepted" : "rejected");
    }

    [Test]
    public async Task AcIso007ExclusiveOutputPreservesExistingFileBytes()
    {
        using var directory = new ImageBundleTestDirectory();
        var result = await IsolatedAggregateNodeProcess.RunAsync(
            ["--input-type=module", "-e", ImageBundleNodeProgram.Exclusive,
                IsolatedAggregateNodeProcess.Module("image-bundle-files.mjs"), directory.Root],
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Output.Trim()).IsEqualTo("preserved");
        await Assert.That(result.Error).IsEmpty();
    }
}
