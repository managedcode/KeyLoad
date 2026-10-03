namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteMetadataRejectionTests
{
    [Test]
    [Arguments("missing-icon")]
    [Arguments("linked-icon")]
    [Arguments("missing-card")]
    [Arguments("linked-card")]
    public async Task AC_SEO_005_RealBuilderRejectsMissingOrLinkedMetadataAssetsBeforeOutput(string condition)
    {
        var inputs = SiteTestInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteMetadataTestScope.CreateAsync(inputs, token);
        var source = SourcePath(scope.Repository, condition);
        if (condition.StartsWith("missing", StringComparison.Ordinal))
        {
            File.Delete(source);
        }
        else
        {
            var target = source + ".target";
            File.Move(source, target);
            File.CreateSymbolicLink(source, target);
        }

        var scopedInputs = new SiteTestInputs(scope.Repository, inputs.Reports, inputs.EvidenceRun,
            inputs.MeasuredRevision, inputs.SiteRevision);
        var rejected = await SiteBuilderProcess.RunAsync(scopedInputs, inputs.Reports, scope.Output, token);
        await Assert.That(rejected.ExitCode).IsNotEqualTo(SiteTokens.ProcessSuccessExitCode);
        await Assert.That(rejected.StandardError.Length).IsGreaterThan(0);
        if (condition.StartsWith("linked", StringComparison.Ordinal))
        {
            await Assert.That(rejected.StandardError).Contains(SiteBuilderTokens.SymlinkError);
        }
        else
        {
            await Assert.That(rejected.StandardError).Contains(Path.GetFileName(source));
        }
        await Assert.That(Directory.Exists(scope.Output)).IsFalse();
    }

    private static string SourcePath(string root, string condition)
    {
        var relative = condition.EndsWith("icon", StringComparison.Ordinal)
            ? Path.Combine(SiteMetadataTokens.SiteDirectory, SiteMetadataTokens.Icon192)
            : Path.Combine(SiteMetadataTokens.FeaturePath, SiteMetadataTokens.SourceCardPng);
        return Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
    }
}

internal sealed class SiteMetadataTestScope : IAsyncDisposable
{
    private SiteMetadataTestScope(string path) => Repository = path;

    internal string Repository { get; }
    internal string Output => Repository + "-" + SiteMetadataTokens.OutputName;

    internal static async Task<SiteMetadataTestScope> CreateAsync(SiteTestInputs inputs, CancellationToken token)
    {
        var path = Path.Combine(Path.GetTempPath(), SiteMetadataTokens.TestDirectoryPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            await CopyTree(Path.Combine(inputs.Repository, SiteMetadataTokens.FeaturePath),
                Path.Combine(path, SiteMetadataTokens.FeaturePath), token);
            await CopyTree(Path.Combine(inputs.Repository, SiteMetadataTokens.AggregateDirectoryPath),
                Path.Combine(path, SiteMetadataTokens.AggregateDirectoryPath), token);
            await CopyTree(Path.Combine(inputs.Repository, "site/scripts"), Path.Combine(path, "site/scripts"), token);
            await CopyFile(inputs.Repository, path, SiteMetadataTokens.FaviconSvgPath, token);
            await CopyFile(inputs.Repository, path, SiteMetadataTokens.ComparisonContractPath, token);
            foreach (var asset in SiteMetadataTokens.IconAssetNames)
            {
                await CopyFile(inputs.Repository, path, Path.Combine(SiteMetadataTokens.SiteDirectory, asset), token);
            }
            return new(path);
        }
        catch (Exception)
        {
            Directory.Delete(path, recursive: true);
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(Repository))
        {
            Directory.Delete(Repository, recursive: true);
        }
        if (Directory.Exists(Output))
        {
            Directory.Delete(Output, recursive: true);
        }
        return ValueTask.CompletedTask;
    }

    private static async Task CopyFile(string sourceRoot, string destinationRoot, string relative, CancellationToken token)
    {
        var source = Path.Combine(sourceRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        var destination = Path.Combine(destinationRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllBytesAsync(destination, await File.ReadAllBytesAsync(source, token), token);
    }

    private static async Task CopyTree(string source, string destination, CancellationToken token)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllBytesAsync(target, await File.ReadAllBytesAsync(file, token), token);
        }
    }
}
