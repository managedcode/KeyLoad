using System.Diagnostics;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteVendorTestScope : IAsyncDisposable
{
    private SiteTempDirectory? _temporary;
    private readonly SiteTestInputs _sourceInputs;
    private SiteTestInputs? InputState { get; set; }

    private SiteVendorTestScope(SiteTestInputs sourceInputs)
    {
        _sourceInputs = sourceInputs;
    }

    internal SiteTestInputs Inputs => InputState ?? throw new InvalidOperationException(SiteVendorTokens.ScopeNotInitialized);
    internal string Output => RequiredTemporary.Output;
    internal string Reports => RequiredTemporary.Reports;
    internal string FeatureSource => Path.Combine(Inputs.Repository, SiteVendorTokens.FeatureDirectory);

    internal static async Task<SiteVendorTestScope> CreateAsync(SiteTestInputs inputs, CancellationToken token)
    {
        var scope = new SiteVendorTestScope(inputs);
        try
        {
            scope.CreateTemporary();
            await CopyFeatureAsync(inputs, scope.FeatureSource, token);
            await CopyBuilderEntryAsync(inputs, scope.Inputs.Repository, token);
            await CopyFaviconAsync(inputs, scope.Inputs.Repository, token);
            await SiteBuildArtifacts.CopyReports(inputs.Reports, scope.Reports, token);
            return scope;
        }
        catch (Exception)
        {
            await scope.DisposeAsync();
            throw;
        }
    }

    internal async Task<JsonObject> ReadManifestAsync(CancellationToken token)
    {
        var path = Path.Combine(FeatureSource, SiteAssetTokens.ThreeVendorRelativePath,
            SiteAssetTokens.ThreeManifestFile);
        var bytes = await File.ReadAllBytesAsync(path, token);
        return JsonNode.Parse(bytes)?.AsObject() ?? throw new InvalidOperationException(SiteVendorTokens.VendorError);
    }

    internal async Task WriteManifestAsync(JsonObject manifest, CancellationToken token)
    {
        var path = Path.Combine(FeatureSource, SiteAssetTokens.ThreeVendorRelativePath,
            SiteAssetTokens.ThreeManifestFile);
        await File.WriteAllTextAsync(path, manifest.ToJsonString(), token);
    }

    internal async Task<string> RunIndependentGzipOracleAsync(CancellationToken token)
    {
        using var process = new Process { StartInfo = CreateOracleStartInfo() };
        if (!process.Start())
        {
            throw new InvalidOperationException(SiteVendorTokens.OracleDidNotStart);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(SiteTokens.NodeTimeoutMilliseconds);
        var stdout = SiteProcessOutput.ReadAsync(process.StandardOutput, SiteTokens.NodeOutputExceeded, timeout.Token);
        var stderr = SiteProcessOutput.ReadAsync(process.StandardError, SiteTokens.NodeOutputExceeded, timeout.Token);
        try
        {
            var exit = process.WaitForExitAsync(timeout.Token);
            await Task.WhenAny(exit, stdout, stderr);
            await exit;
            var output = await stdout;
            var error = await stderr;
            if (process.ExitCode != SiteTokens.ProcessSuccessExitCode || error.Length != SiteTokens.Zero)
            {
                throw new InvalidOperationException(SiteVendorTokens.OracleFailure);
            }

            return output;
        }
        catch (Exception)
        {
            try
            {
                await SiteProcessCleanup.StopAsync(process);
            }
            finally
            {
                await SiteProcessCleanup.ObserveCapturesAsync(process, stdout, stderr);
            }

            throw;
        }
    }

    internal async Task AssertOriginalVendorUnchangedAsync(CancellationToken token)
    {
        foreach (var name in SiteVendorTokens.VendorFiles)
        {
            var original = Path.Combine(_sourceInputs.Repository, SiteVendorTokens.FeatureDirectory,
                SiteAssetTokens.ThreeVendorRelativePath, name);
            var emitted = Path.Combine(Output, SiteVendorTokens.FeatureOutputDirectory,
                SiteAssetTokens.ThreeVendorRelativePath, name);
            var expected = await File.ReadAllBytesAsync(original, token);
            var actual = await File.ReadAllBytesAsync(emitted, token);
            await Assert.That(actual.SequenceEqual(expected)).IsTrue();
        }
    }

    internal async Task AssertEmittedManifestMatchesCopyAsync(CancellationToken token)
    {
        var source = Path.Combine(FeatureSource, SiteAssetTokens.ThreeVendorRelativePath,
            SiteAssetTokens.ThreeManifestFile);
        var emitted = Path.Combine(Output, SiteVendorTokens.FeatureOutputDirectory,
            SiteAssetTokens.ThreeVendorRelativePath, SiteAssetTokens.ThreeManifestFile);
        var sourceBytes = await File.ReadAllBytesAsync(source, token);
        var emittedBytes = await File.ReadAllBytesAsync(emitted, token);
        await Assert.That(emittedBytes.SequenceEqual(sourceBytes)).IsTrue();
    }

    public ValueTask DisposeAsync()
    {
        var temporary = _temporary;
        return temporary is null ? ValueTask.CompletedTask : temporary.DisposeAsync();
    }

    private SiteTempDirectory RequiredTemporary => _temporary ??
        throw new InvalidOperationException(SiteVendorTokens.ScopeNotInitialized);

    private void CreateTemporary()
    {
        _temporary = SiteTempDirectory.Create();
        InputState = new(_temporary.Path, _temporary.Reports, _sourceInputs.EvidenceRun,
            _sourceInputs.MeasuredRevision, _sourceInputs.SiteRevision);
    }

    private static async Task CopyFeatureAsync(SiteTestInputs inputs, string destination, CancellationToken token)
    {
        var source = Path.Combine(inputs.Repository, SiteVendorTokens.FeatureDirectory);
        await CopyDirectoryAsync(source, destination, token);
    }

    private static async Task CopyBuilderEntryAsync(SiteTestInputs inputs, string destination, CancellationToken token)
    {
        var source = Path.Combine(inputs.Repository, SiteVendorTokens.BuildEntry);
        var target = Path.Combine(destination, SiteVendorTokens.BuildEntry);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllBytesAsync(target, await File.ReadAllBytesAsync(source, token), token);
    }

    private static async Task CopyFaviconAsync(SiteTestInputs inputs, string destination, CancellationToken token)
    {
        var source = Path.Combine(inputs.Repository, SiteAssetTokens.FaviconSourcePath);
        var target = Path.Combine(destination, SiteAssetTokens.FaviconSourcePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllBytesAsync(target, await File.ReadAllBytesAsync(source, token), token);
    }

    private static async Task CopyDirectoryAsync(string source, string destination, CancellationToken token)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, SiteVendorTokens.SearchAllEntries,
                     SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, SiteVendorTokens.SearchAllEntries, SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllBytesAsync(target, await File.ReadAllBytesAsync(file, token), token);
        }
    }

    private ProcessStartInfo CreateOracleStartInfo()
    {
        var start = new ProcessStartInfo(SiteTokens.NodeExecutable)
        {
            WorkingDirectory = Inputs.Repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(SiteVendorTokens.NodeTypeArgument);
        start.ArgumentList.Add(SiteVendorTokens.NodeEvalArgument);
        start.ArgumentList.Add(SiteVendorTokens.GzipOracleProgram);
        var root = Path.Combine(FeatureSource, SiteAssetTokens.ThreeVendorRelativePath);
        foreach (var name in SiteVendorTokens.VendorFiles)
        {
            start.ArgumentList.Add(Path.Combine(root, name));
        }

        return start;
    }
}
