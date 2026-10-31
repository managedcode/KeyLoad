using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

/// <summary>One test's own directory below the test output: downloaded artifacts, merged file and built site.</summary>
internal sealed class SiteWorkspace : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private SiteWorkspace(string path)
    {
        Path = path;
    }

    internal string Path { get; }

    internal string Input => System.IO.Path.Combine(Path, "input");

    internal string Merged => System.IO.Path.Combine(Path, "merged", SiteTokens.MergedFile);

    internal string Site => System.IO.Path.Combine(Path, "site");

    internal static SiteWorkspace Create()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "website-runs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new(path);
    }

    // Copies whole artifact folders, including the test logs that the merge must leave out.
    internal async Task CopyArtifactsAsync(IEnumerable<string> artifacts, CancellationToken cancellationToken)
    {
        foreach (var artifact in artifacts)
        {
            var source = System.IO.Path.Combine(SiteResultFiles.Root, artifact);
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = System.IO.Path.Combine(Input, artifact, System.IO.Path.GetRelativePath(source, file));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                await File.WriteAllBytesAsync(target, await File.ReadAllBytesAsync(file, cancellationToken), cancellationToken);
            }
        }
    }

    internal async Task WriteResultAsync(string artifact, string fileName, JsonObject result, CancellationToken cancellationToken)
    {
        var directory = System.IO.Path.Combine(Input, artifact);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(System.IO.Path.Combine(directory, fileName), result.ToJsonString(Indented), cancellationToken);
    }

    internal Task<SiteNodeResult> MergeAsync(CancellationToken cancellationToken) => SiteNodeProcess.RunAsync(
        SiteTokens.MergeScript, [SiteTokens.InputArgument + Input, SiteTokens.OutputArgument + Merged], cancellationToken);

    internal Task<SiteNodeResult> BuildAsync(string results, CancellationToken cancellationToken) => SiteNodeProcess.RunAsync(
        SiteTokens.BuildScript, [SiteTokens.ResultsArgument + results, SiteTokens.OutputArgument + Site,
            SiteTokens.SiteRevisionArgument + SiteTokens.SiteRevision], cancellationToken);

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}
