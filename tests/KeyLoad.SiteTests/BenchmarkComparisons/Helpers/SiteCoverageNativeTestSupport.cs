using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed record SiteCoverageNativeReceipt(byte[] Bytes, string Identity, string Sha256,
    string StandardOutput);

internal static class SiteCoverageNativeTestSupport
{
    public const string FixtureSource = "const marker = \"🟩\";\r\nfunction choose(flag) {\r\n  if (flag) {\r\n    const lazy = () => \"unused\"; return marker;\r\n  } else {\r\n    return \"none\";\r\n  }}\r\nfunction neverCalled() { return 123; }\r\nconsole.log(choose(process.argv[2] === \"yes\"));\r\n";
    public const string TrueArgument = "yes";
    public const string FalseArgument = "no";
    public const string FixtureRelativePath = SiteCoverageTokens.FixtureRelativePath;
    public const string EqualSpanFunctionName = "neverCalled";
    public const string EqualSpanFixtureSource = "function neverCalled() { return 1; }";

    public static Task<SiteCoverageNativeReceipt> RunNodeCoverageAsync(SiteCoverageFixtureDirectory temporary,
        string argument, CancellationToken cancellationToken) =>
        RunNodeCoverageForAsync(temporary, argument, FixtureRelativePath, cancellationToken);

    public static async Task<SiteCoverageNativeReceipt> RunNodeCoverageForAsync(SiteCoverageFixtureDirectory temporary,
        string argument, string fixturePath, CancellationToken cancellationToken)
    {
        var coverageDirectory = Path.Combine(temporary.Root, argument);
        Directory.CreateDirectory(coverageDirectory);
        var startInfo = new ProcessStartInfo(SiteTokens.NodeExecutable)
        {
            WorkingDirectory = temporary.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(Path.Combine(temporary.Root,
            fixturePath.Replace(SiteCoverageTokens.RelativeSeparator, Path.DirectorySeparatorChar)));
        startInfo.ArgumentList.Add(argument);
        startInfo.Environment[SiteCoverageTokens.NodeCoverageEnvironment] = coverageDirectory;
        var process = await SiteCoverageNodeProcess.RunAsync(startInfo, cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != SiteTokens.ProcessSuccessExitCode || process.StandardError.Length != SiteCoverageTokens.Zero)
        {
            throw new InvalidOperationException(SiteCoverageTokens.InvalidCoverageFailure);
        }

        var path = Directory.GetFiles(coverageDirectory, SiteCoverageTokens.NodeCoverageFilePrefix +
            SiteCoverageTokens.Wildcard + SiteCoverageTokens.JsonExtension, SearchOption.TopDirectoryOnly).Single();
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var identity = Path.GetRelativePath(temporary.Root, path).Replace(Path.DirectorySeparatorChar,
            SiteCoverageTokens.RelativeSeparator);
        return new(bytes, identity, SiteCoverageSourceManifestWriter.Hash(bytes), process.StandardOutput);
    }

    public static IReadOnlyList<SiteCoverageScriptSnapshot> ParseSnapshots(SiteCoverageNativeReceipt receipt,
        string root, IReadOnlyDictionary<string, SiteCoverageSourceEntry> sources)
    {
        using var document = JsonDocument.Parse(receipt.Bytes);
        SiteCoverageArtifactMetadata.ValidateEnvelope(document.RootElement, SiteCoverageTokens.NodeRuntime);
        return SiteCoverageNativeRanges.ParseScripts(document.RootElement.GetProperty(SiteCoverageTokens.Result),
            SiteCoverageTokens.NodeRuntime, root, sources, null, receipt.Identity, receipt.Sha256);
    }

    public static async Task AssertSnapshotIdentity(SiteCoverageNativeReceipt receipt,
        IReadOnlyList<SiteCoverageScriptSnapshot> snapshots, string root)
    {
        var script = FindFixtureScript(JsonNode.Parse(receipt.Bytes)!.AsObject(), root);
        var expectedContext = script.TryGetPropertyValue(SiteCoverageTokens.ExecutionContextId, out var context)
            ? context!.GetValue<int>() : (int?)null;
        await Assert.That(snapshots.Count).IsEqualTo(SiteCoverageTokens.One);
        var snapshot = snapshots[SiteCoverageTokens.Zero];
        await Assert.That(snapshot.ReceiptIdentity).IsEqualTo(receipt.Identity);
        await Assert.That(snapshot.ReceiptSha256).IsEqualTo(receipt.Sha256);
        await Assert.That(snapshot.ScriptId).IsEqualTo(script[SiteCoverageTokens.ScriptId]!.GetValue<string>());
        await Assert.That(snapshot.ExecutionContextId).IsEqualTo(expectedContext);
    }

    internal static JsonObject FindFixtureScript(JsonObject receipt, string root)
    {
        var sourcePath = Path.GetFullPath(Path.Combine(root,
            FixtureRelativePath.Replace(SiteCoverageTokens.RelativeSeparator, Path.DirectorySeparatorChar)));
        var expectedUrl = new Uri(sourcePath).AbsoluteUri;
        var scripts = receipt[SiteCoverageTokens.Result]!.AsArray();
        var selected = scripts.Select(node => node!.AsObject()).Single(script =>
            script[SiteCoverageTokens.Url]!.GetValue<string>() == expectedUrl);
        var scriptId = selected[SiteCoverageTokens.ScriptId]!.GetValue<string>();
        return scripts.Select(node => node!.AsObject()).Single(script =>
            script[SiteCoverageTokens.Url]!.GetValue<string>() == expectedUrl &&
            script[SiteCoverageTokens.ScriptId]!.GetValue<string>() == scriptId);
    }

}
