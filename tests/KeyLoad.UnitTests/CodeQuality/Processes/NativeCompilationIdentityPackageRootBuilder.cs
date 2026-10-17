using System.Diagnostics;
using KeyLoad.UnitTests.Features.CodeQuality.Helpers;

namespace KeyLoad.UnitTests.Features.CodeQuality.Processes;

internal static class NativeCompilationIdentityPackageRootBuilder
{
    private const string Dotnet = "dotnet";
    private const string Build = "build";
    private const string Release = "Release";
    private const string Framework = "net10.0";
    private const string Quiet = "quiet";
    private const string ConfigurationOption = "--configuration";
    private const string FrameworkOption = "--framework";
    private const string VerbosityOption = "--verbosity";
    private const string NoIncrementalOption = "--no-incremental";
    private const string PropertyPrefix = "-p:";
    private const string NuGetPackageRootProperty = "NuGetPackageRoot=";
    private const string TUnitPropsProperty = "TUnitPropsPath=";
    private const string TUnitSourceProperty = "TUnitGeneratedSource=";
    private const string CounterfeitProperty = "IncludeCounterfeit=";
    private const string EnabledValue = "true";
    private const string DisabledValue = "false";

    internal static async Task<ProductionSourceManifestProcessResult> BuildAsync(
        NativeCompilationIdentityPackageRootFixture fixture, bool trailingSeparator, bool counterfeit, bool forceCompile,
        CancellationToken cancellationToken)
    {
        var root = ResolvePackageRoot(fixture.TUnitPropsPath, trailingSeparator);
        var start = new ProcessStartInfo(Dotnet)
        {
            WorkingDirectory = fixture.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(Build);
        start.ArgumentList.Add(fixture.ProjectPath);
        start.ArgumentList.Add(ConfigurationOption);
        start.ArgumentList.Add(Release);
        start.ArgumentList.Add(FrameworkOption);
        start.ArgumentList.Add(Framework);
        if (forceCompile)
        {
            start.ArgumentList.Add(NoIncrementalOption);
        }
        start.ArgumentList.Add(VerbosityOption);
        start.ArgumentList.Add(Quiet);
        start.ArgumentList.Add(PropertyPrefix + NuGetPackageRootProperty + root);
        start.ArgumentList.Add(PropertyPrefix + TUnitPropsProperty + fixture.TUnitPropsPath);
        start.ArgumentList.Add(PropertyPrefix + TUnitSourceProperty + fixture.TUnitSourcePath);
        start.ArgumentList.Add(PropertyPrefix + CounterfeitProperty + (counterfeit ? EnabledValue : DisabledValue));
        return await ProductionSourceManifestProcess.RunAsync(fixture.ExecutionOptions, start, cancellationToken);
    }

    private static string ResolvePackageRoot(string propsPath, bool trailingSeparator)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(propsPath)!).Parent!.Parent!.Parent!.Parent!.FullName;
        var normalized = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trailingSeparator ? normalized + Path.DirectorySeparatorChar : normalized;
    }
}
