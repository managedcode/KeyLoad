namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed record SiteProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal sealed class SiteTempDirectory(string path) : IAsyncDisposable
{
    public string Path { get; } = path;
    public string Output => System.IO.Path.Combine(Path, SiteAssetTokens.OutputDirectory);

    public static SiteTempDirectory Create()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"{SiteAssetTokens.TempDirectoryPrefix}{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return new(path);
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}
