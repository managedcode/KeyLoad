namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

/// <summary>Source-bound parser/projection closure for the complete and historical comparison families.</summary>
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
        $"{SitePublicationTokens.EvidenceToolsPrefix}historical-isolated-plan.mjs",
        $"{SitePublicationTokens.EvidenceToolsPrefix}historical-site-evidence.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/composite-render.mjs",
        $"{SiteAssetTokens.FeatureRelativePath}/historical-contracts.mjs",
    ];
}
