namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

/// <summary>Source-bound parser/projection closure for the complete current comparison cohort.</summary>
internal static class SiteCompositeCoverageSources
{
    internal static readonly string[] Entries =
    [
        $"{SitePublicationTokens.EvidenceToolsPrefix}composite-site-contract.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}composite-site-evidence.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}scaled-isolated-plan.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}vector-isolated-plan.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}scaled-cohort-receipt.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}server-resource-evidence.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}document-isolated-plan.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}document-evidence.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}document-worker-finalize.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/composite-render.mjs",
    ];
}
