namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Exercises confined runtime extraction and exact compiled-image identity admission.</summary>
internal sealed class CompiledBenchmarkRuntimeBundleTests
{
    [Test]
    [Arguments("valid")]
    [Arguments("source-path")]
    [Arguments("traversal")]
    [Arguments("backslash")]
    [Arguments("duplicate")]
    [Arguments("oversized")]
    [Arguments("foreign-source")]
    [Arguments("foreign-attempt")]
    [Arguments("foreign-workspace")]
    [Arguments("foreign-dll")]
    [Arguments("foreign-pdb")]
    [Arguments("foreign-native")]
    [Arguments("missing-native")]
    public async Task RuntimeAdmissionRejectsCorruptionAndPreservesTheOriginalHealthyIdentity(string variant)
    {
        var result = await SessionNodeProcess.RunAsync(
            ["--input-type=module", "-e", CompiledBenchmarkRuntimeNodeProgram.Contract,
                SessionNodeProcess.Module("compiled-test-bundle-contract.mjs"), variant],
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Error);
        await Assert.That(result.Output.Trim()).IsEqualTo("runtime-contract-accepted");
        await Assert.That(result.Error).IsEmpty();
    }

    [Test]
    [Arguments("valid")]
    [Arguments("foreign-target")]
    [Arguments("stale-version")]
    [Arguments("arbitrary-link")]
    public async Task NativeAspireBundleLinkIsOmittedWhileForeignRuntimeLinksAreRejected(string variant)
    {
        var result = await SessionNodeProcess.RunAsync(
            ["--input-type=module", "-e", CompiledBenchmarkRuntimeInventoryProgram.Program,
                SessionNodeProcess.Module("compiled-test-bundle-files.mjs"), SessionNodeProcess.RepositoryRoot(), variant],
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Error);
        await Assert.That(result.Output.Trim()).IsEqualTo("runtime-inventory-accepted");
        await Assert.That(result.Error).IsEmpty();
    }
}
