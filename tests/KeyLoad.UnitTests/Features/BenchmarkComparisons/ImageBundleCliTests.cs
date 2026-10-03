namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ImageBundleCliTests
{
    [Test]
    [Arguments("export-images.mjs", "--foreign=value")]
    [Arguments("import-images.mjs", "--foreign=value")]
    [Arguments("import-images.mjs", "--bundle=relative")]
    [Arguments("import-images.mjs", "--bundle=")]
    public async Task AcIso007CliRejectsInvalidOptionsBeforeNativeAllocation(string module, string argument)
    {
        var result = await IsolatedAggregateNodeProcess.RunAsync(
            [IsolatedAggregateNodeProcess.Module(module), argument], TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.Output).IsEmpty();
        await Assert.That(result.Error.Trim()).IsEqualTo(
            argument.StartsWith("--bundle=", StringComparison.Ordinal)
                ? "The image bundle path is unsafe."
                : module == "export-images.mjs"
                    ? "The native image bundle operation failed."
                    : "The image bundle input is invalid.");
    }

    [Test]
    [Arguments("export-images.mjs")]
    [Arguments("import-images.mjs")]
    public async Task AcIso007CliRejectsMissingTrustedContextOrRequiredBundle(string module)
    {
        var result = await IsolatedAggregateNodeProcess.RunAsync(
            [IsolatedAggregateNodeProcess.Module(module)], TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.Output).IsEmpty();
        await Assert.That(result.Error.Trim()).IsEqualTo(module == "import-images.mjs"
            ? "The image bundle input is invalid." : "The native image bundle operation failed.");
    }
}
