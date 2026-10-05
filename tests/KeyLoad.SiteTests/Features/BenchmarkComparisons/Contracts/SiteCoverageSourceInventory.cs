namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteCoverageSourceInventory
{
    public static readonly string[] ProductionSources =
    [
        .. SiteCompositeCoverageSources.Entries,
        $"{SiteAssetTokens.FeatureRelativePath}/measured-build.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/isolated-contracts.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/isolated-metadata.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/isolated-metrics-validation.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/isolated-report-validation.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/isolated-projection.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/isolated-http.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/isolated-loader.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/isolated-measurements.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/isolated-view.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/isolated-controls.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/isolated-lab.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/measurements.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/measurement-loader.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/contracts.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/bootstrap.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/build-site.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/cluster-scene.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/scene-geometry.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/scene-lifecycle.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/scene-observers.mjs",
        SiteCoverageTokens.BuildScriptSource,
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-api.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-capture.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-cli.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-context.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-contract.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-files.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-fresh.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-native-proof.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-proof.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-receipt.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-runs.mjs",
    ];

    public static readonly string[] ContentSources =
    [
        SiteCoverageTokens.BuildScriptSource,
        $"{SiteAssetTokens.FeatureRelativePath}/build-site.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/bootstrap.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/contracts.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/cluster-scene.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/scene-geometry.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/scene-lifecycle.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/scene-observers.mjs",
    ];

    public static readonly string[] MeasuredCriticalSources =
    [
        .. SiteCompositeCoverageSources.Entries,
        $"{SiteAssetTokens.FeatureRelativePath}/measured-build.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/isolated-contracts.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/isolated-metadata.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/isolated-metrics-validation.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/isolated-report-validation.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/isolated-projection.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/isolated-http.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/isolated-loader.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/isolated-measurements.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/isolated-view.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/isolated-controls.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/isolated-lab.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/measurements.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/measurement-loader.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/build-site.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-api.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-capture.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-cli.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-context.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-contract.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-files.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-fresh.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-native-proof.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-proof.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-receipt.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}site-isolated-github-runs.mjs",
    ];

    public static readonly string[] ContentCriticalSources =
    [
        $"{SiteAssetTokens.FeatureRelativePath}/build-site.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/bootstrap.mjs",
    ];

    public static readonly string[] TrackedEvidenceModulePrefixes =
    [
        SitePublicationTokens.IsolatedEvidenceModulePrefix,
        "composite-site-",
        "scaled-isolated-plan",
        "vector-isolated-plan",
        "scaled-cohort-",
        "server-resource-",
        "historical-",
    ];

    public static bool IsTrackedEvidenceModule(string path) =>
        TrackedEvidenceModulePrefixes.Any(prefix => System.IO.Path.GetFileName(path)
            .StartsWith(prefix, StringComparison.Ordinal));
}
