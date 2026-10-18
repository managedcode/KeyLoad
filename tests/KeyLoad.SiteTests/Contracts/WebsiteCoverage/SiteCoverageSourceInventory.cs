namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

/// <summary>Current executable session intake and browser source inventory.</summary>
internal static class SiteCoverageSourceInventory
{
    public static readonly string[] ProductionSources =
    [
        "scripts/Features/BenchmarkComparisons/aggregate-contracts.mjs",
        "scripts/Features/BenchmarkComparisons/aggregate-files.mjs",
        "scripts/Features/BenchmarkComparisons/aggregate-json.mjs",
        "scripts/Features/BenchmarkComparisons/compiled-test-bundle-contract.mjs",
        "scripts/Features/BenchmarkComparisons/document-functional-gate.mjs",
        "scripts/Features/BenchmarkComparisons/functional-process-evidence.mjs",
        "scripts/Features/BenchmarkComparisons/native-files.mjs",
        "scripts/Features/BenchmarkComparisons/native-process-contracts.mjs",
        "scripts/Features/BenchmarkComparisons/native-process.mjs",
        "scripts/Features/BenchmarkComparisons/isolated-github-api.mjs",
        "scripts/Features/BenchmarkComparisons/isolated-github-contract.mjs",
        "scripts/Features/BenchmarkComparisons/isolated-github-files.mjs",
        "scripts/Features/BenchmarkComparisons/isolated-github-headers.mjs",
        "scripts/Features/BenchmarkComparisons/isolated-github-rate.mjs",
        "scripts/Features/BenchmarkComparisons/isolated-github-stream.mjs",
        "scripts/Features/BenchmarkComparisons/isolated-github-transport.mjs",
        "scripts/Features/BenchmarkComparisons/isolated-github-validation.mjs",
        "scripts/Features/BenchmarkComparisons/isolated-github-zip.mjs",
        "scripts/Features/BenchmarkComparisons/server-resource-evidence.mjs",
        "scripts/Features/BenchmarkComparisons/session-aggregate-contract.mjs",
        "scripts/Features/BenchmarkComparisons/session-aggregate.mjs",
        "scripts/Features/BenchmarkComparisons/session-contract.mjs",
        "scripts/Features/BenchmarkComparisons/session-github-selection.mjs",
        "scripts/Features/BenchmarkComparisons/session-original-archives.mjs",
        "scripts/Features/BenchmarkComparisons/session-plan-contract.mjs",
        "scripts/Features/BenchmarkComparisons/session-plan.mjs",
        "scripts/Features/BenchmarkComparisons/session-process-identity.mjs",
        "scripts/Features/BenchmarkComparisons/session-report-validation.mjs",
        "scripts/Features/BenchmarkComparisons/session-runtime-identity.mjs",
        "scripts/Features/BenchmarkComparisons/session-source-closure.mjs",
        "scripts/Features/BenchmarkComparisons/site-isolated-github-cli.mjs",
        "scripts/Features/BenchmarkComparisons/site-isolated-github-receipt.mjs",
        "scripts/Features/BenchmarkComparisons/site-session-context.mjs",
        "scripts/Features/BenchmarkComparisons/site-session-github.mjs",
        "scripts/Features/BenchmarkComparisons/site-session-proof.mjs",
        "scripts/Features/BenchmarkComparisons/site-session-receipt.mjs",
        "scripts/Features/BenchmarkComparisons/site-session-selection.mjs",
        "site/Features/BenchmarkComparisons/bootstrap.mjs",
        "site/Features/BenchmarkComparisons/build-site.mjs",
        "site/Features/BenchmarkComparisons/cluster-scene.mjs",
        "site/Features/BenchmarkComparisons/contracts.mjs",
        "site/Features/BenchmarkComparisons/isolated-contracts.mjs",
        "site/Features/BenchmarkComparisons/isolated-controls.mjs",
        "site/Features/BenchmarkComparisons/isolated-http.mjs",
        "site/Features/BenchmarkComparisons/isolated-lab.mjs",
        "site/Features/BenchmarkComparisons/isolated-loader.mjs",
        "site/Features/BenchmarkComparisons/isolated-measurements.mjs",
        "site/Features/BenchmarkComparisons/isolated-projection.mjs",
        "site/Features/BenchmarkComparisons/isolated-view.mjs",
        "site/Features/BenchmarkComparisons/measured-build.mjs",
        "site/Features/BenchmarkComparisons/measurement-loader.mjs",
        "site/Features/BenchmarkComparisons/measurements.mjs",
        "site/Features/BenchmarkComparisons/scene-geometry.mjs",
        "site/Features/BenchmarkComparisons/scene-lifecycle.mjs",
        "site/Features/BenchmarkComparisons/scene-observers.mjs",
        "site/scripts/build.mjs",
    ];
    public static readonly string[] ContentSources =
    [
        "site/scripts/build.mjs",
        "site/Features/BenchmarkComparisons/build-site.mjs",
        "site/Features/BenchmarkComparisons/bootstrap.mjs",
        "site/Features/BenchmarkComparisons/contracts.mjs",
        "site/Features/BenchmarkComparisons/cluster-scene.mjs",
        "site/Features/BenchmarkComparisons/scene-geometry.mjs",
        "site/Features/BenchmarkComparisons/scene-lifecycle.mjs",
        "site/Features/BenchmarkComparisons/scene-observers.mjs",
    ];
    public static readonly string[] MeasuredCriticalSources =
    [
        "site/Features/BenchmarkComparisons/measured-build.mjs",
        "site/Features/BenchmarkComparisons/isolated-projection.mjs",
        "site/Features/BenchmarkComparisons/isolated-loader.mjs",
        "site/Features/BenchmarkComparisons/isolated-measurements.mjs",
        "site/Features/BenchmarkComparisons/isolated-view.mjs",
        "site/Features/BenchmarkComparisons/isolated-controls.mjs",
        "site/Features/BenchmarkComparisons/isolated-lab.mjs",
        "site/Features/BenchmarkComparisons/measurements.mjs",
        "site/Features/BenchmarkComparisons/measurement-loader.mjs",
        "site/Features/BenchmarkComparisons/build-site.mjs",
        "scripts/Features/BenchmarkComparisons/session-contract.mjs",
        "scripts/Features/BenchmarkComparisons/session-plan-contract.mjs",
        "scripts/Features/BenchmarkComparisons/session-report-validation.mjs",
        "scripts/Features/BenchmarkComparisons/session-process-identity.mjs",
        "scripts/Features/BenchmarkComparisons/functional-process-evidence.mjs",
        "scripts/Features/BenchmarkComparisons/session-aggregate-contract.mjs",
        "scripts/Features/BenchmarkComparisons/site-session-selection.mjs",
        "scripts/Features/BenchmarkComparisons/site-session-receipt.mjs",
        "scripts/Features/BenchmarkComparisons/site-session-proof.mjs",
    ];
    public static readonly string[] ContentCriticalSources =
    [
        "site/Features/BenchmarkComparisons/build-site.mjs",
        "site/Features/BenchmarkComparisons/bootstrap.mjs",
    ];
    public static bool IsTrackedEvidenceModule(string path) =>
        ProductionSources.Contains(path, StringComparer.Ordinal) ||
        System.IO.Path.GetFileName(path).StartsWith("site-isolated-github-", StringComparison.Ordinal) ||
        System.IO.Path.GetFileName(path).StartsWith("site-session-", StringComparison.Ordinal);
}
