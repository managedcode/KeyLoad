namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Resolves and copies the existing per-profile comparison evidence files.</summary>
internal static class ComparisonTestEvidenceFiles
{
    internal static string GetDirectory()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository.Parent is not null && !File.Exists(Path.Combine(repository.FullName, "KeyLoad.slnx")))
        {
            repository = repository.Parent;
        }

        var reportName = Environment.GetEnvironmentVariable("KEYLOAD_COMPARISON_REPORT_NAME") ?? "smoke";
        return Path.Combine(repository.FullName, "artifacts", "comparisons", reportName);
    }

    internal static void CopyReportsIfPresent(string output, string evidence)
    {
        if (!File.Exists(Path.Combine(output, "results.json")))
        {
            return;
        }

        Directory.CreateDirectory(evidence);
        foreach (var file in Directory.EnumerateFiles(output))
        {
            File.Copy(file, Path.Combine(evidence, Path.GetFileName(file)), true);
        }
    }
}
