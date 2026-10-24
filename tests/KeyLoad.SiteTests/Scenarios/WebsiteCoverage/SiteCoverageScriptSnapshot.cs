namespace KeyLoad.SiteTests.BenchmarkComparisons;

internal sealed record SiteCoverageScriptSnapshot(string ReceiptIdentity, string ReceiptSha256, string ScriptId,
    int? ExecutionContextId, string SourcePath, IReadOnlyList<SiteCoverageFunctionRanges> Functions);
