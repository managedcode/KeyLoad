using System.Diagnostics;
using System.Runtime.InteropServices;
using KeyLoad.UnitTests.Features.CodeQuality.Assertions;
using KeyLoad.UnitTests.Features.CodeQuality.Helpers;

namespace KeyLoad.UnitTests.Features.CodeQuality.Processes;

internal static class PathMapCompiler
{
    private const string Dotnet = "dotnet";
    private const string VersionArgument = "--version";
    private const string ExecArgument = "exec";
    private const string SdkDirectory = "sdk";
    private const string RoslynDirectory = "Roslyn";
    private const string CompilerDirectory = "bincore";
    private const string CompilerFileName = "csc.dll";
    private const string MissingCompiler = "The installed execution SDK compiler could not be resolved.";
    private static readonly string[] CompilerSwitches =
        ["-nologo", "-noconfig", "-nostdlib+", "-target:library", "-debug:portable",
            "-deterministic+", "-checksumalgorithm:SHA256", "-optimize-"];

    internal static Task CompileAsync(PathMapFixture fixture, string mappedRoot,
        CancellationToken cancellationToken)
        => CompileCoreAsync(fixture, mappedRoot, [], cancellationToken);

    internal static Task CompileAdditionalAsync(PathMapFixture fixture, string additionalSource,
        CancellationToken cancellationToken)
        => CompileCoreAsync(fixture, PathMapFixture.CanonicalMap, [additionalSource], cancellationToken);

    private static async Task CompileCoreAsync(PathMapFixture fixture, string mappedRoot,
        IReadOnlyList<string> additionalSources, CancellationToken cancellationToken)
    {
        await File.WriteAllBytesAsync(fixture.SourcePath, PathMapFixture.OriginalSourceBytes, cancellationToken);
        var versionStart = CreateStartInfo(ProductionSourceManifestProcess.RepositoryRoot);
        versionStart.ArgumentList.Add(VersionArgument);
        var discovered = await ProductionSourceManifestProcess.RunAsync(fixture.ExecutionOptions,
            versionStart, cancellationToken);
        await PathMapAssertions.SuccessfulProcessAsync(discovered);
        var compiler = ResolveCompiler(discovered.StandardOutput.Trim());
        var start = CreateStartInfo(fixture.Root);
        start.ArgumentList.Add(ExecArgument);
        start.ArgumentList.Add(compiler);
        foreach (var argument in CompilerSwitches)
        {
            start.ArgumentList.Add(argument);
        }
        start.ArgumentList.Add("-reference:" + typeof(object).Assembly.Location);
        start.ArgumentList.Add("-out:" + fixture.DllPath);
        start.ArgumentList.Add("-pdb:" + fixture.PdbPath);
        start.ArgumentList.Add("-pathmap:" + fixture.Root + "=" + mappedRoot);
        start.ArgumentList.Add(fixture.SourcePath);
        foreach (var source in additionalSources)
        {
            start.ArgumentList.Add(source);
        }
        var compiled = await ProductionSourceManifestProcess.RunAsync(fixture.ExecutionOptions, start, cancellationToken);
        await PathMapAssertions.SuccessfulProcessAsync(compiled);
        await PathMapAssertions.RegularCompilerFilesAsync(fixture);
        if (additionalSources.Count == 0)
        {
            await PathMapAssertions.RuntimeDocumentAsync(fixture, mappedRoot);
        }
    }

    private static string ResolveCompiler(string version)
    {
        var runtime = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory());
        var runtimeRoot = runtime.Parent?.Parent?.Parent?.FullName;
        if (runtimeRoot is null || string.IsNullOrWhiteSpace(version)
            || version.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            throw new InvalidOperationException(MissingCompiler);
        }
        var compiler = Path.Combine(runtimeRoot, SdkDirectory, version, RoslynDirectory, CompilerDirectory, CompilerFileName);
        return File.Exists(compiler) ? compiler : throw new FileNotFoundException(MissingCompiler);
    }

    private static ProcessStartInfo CreateStartInfo(string directory) => new(Dotnet)
    {
        WorkingDirectory = directory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
}
