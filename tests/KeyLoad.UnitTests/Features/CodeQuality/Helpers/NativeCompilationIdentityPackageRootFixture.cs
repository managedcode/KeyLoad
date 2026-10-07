using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.UnitTests.Features.CodeQuality.Processes;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.CodeQuality.Helpers;

internal sealed class NativeCompilationIdentityPackageRootFixture : IAsyncDisposable
{
    internal const string ProjectRelativePath = "tests/KeyLoad.UnitTests/KeyLoad.UnitTests.csproj";
    internal const string SourceRelativePath = "tests/KeyLoad.UnitTests/Owned.cs";
    internal const string ProducerRelativePath = "tests/KeyLoad.UnitTests/Features/CodeQuality/Build/FunctionalCompilationIdentity.targets";
    internal const string CounterfeitRelativePath = "tests/KeyLoad.UnitTests/Counterfeit.props";
    private const string FixturePrefix = "keyload-native-compile-package-root-";
    private const string GuidFormat = "N";
    private const string CentralPackageFile = "Directory.Packages.props";
    private const string NuGetConfigFile = "NuGet.Config";
    private const string ProjectAssetsPath = "tests/KeyLoad.UnitTests/obj/project.assets.json";
    private const string PackageVersionElement = "PackageVersion";
    private const string PackageIncludeAttribute = "Include";
    private const string PackageVersionAttribute = "Version";
    private const string PinnedTestPackage = "TUnit";
    private const string PackageFoldersProperty = "packageFolders";
    private const string TUnitPackageFolder = "tunit.core";
    private const string BuildTransitiveDirectory = "buildTransitive";
    private const string TUnitPropsFile = "TUnit.Core.props";
    private const string TUnitSourceFile = "TUnit.Core.GeneratedNamespace.cs";
    private const string MissingPinnedPackage = "The resolved pinned TUnit native props and source are unavailable.";
    private const string MissingTUnitPin = "The central pinned TUnit version is missing.";
    private const string TestAssemblyName = "NativeCompilationIdentityPackageRootFixture";
    private const string TargetFramework = "net10.0";
    private const string OwnedSource = "namespace PackageRootFixture; internal static class Owned { internal static int Value => 42; }";
    private const string CounterfeitProps = "<Project><ItemGroup><Compile Remove=\"$(TUnitGeneratedSource)\" /><Compile Include=\"$(TUnitGeneratedSource)\" Link=\"TUnit.Core.GeneratedNamespace.cs\" Visible=\"false\" /></ItemGroup></Project>";
    private const string ProjectTemplate = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <AssemblyName>NativeCompilationIdentityPackageRootFixture</AssemblyName>
            <EnableDefaultCompileItems>true</EnableDefaultCompileItems>
            <GenerateAssemblyInfo>true</GenerateAssemblyInfo>
            <DebugType>portable</DebugType>
            <Deterministic>true</Deterministic>
          </PropertyGroup>
          <Import Project="$(TUnitPropsPath)" />
          <Import Project="Counterfeit.props" Condition="'$(IncludeCounterfeit)' == 'true'" />
          <ItemGroup>
            <ProjectReference Remove="$([System.IO.Path]::GetFullPath('$(MSBuildProjectDirectory)/../../src/KeyLoad.Analyzers/KeyLoad.Analyzers.csproj'))" />
          </ItemGroup>
        </Project>
        """;
    private static readonly string[] CentralFiles =
    ["global.json", "Directory.Build.props", "Directory.Build.targets", CentralPackageFile, "KeyLoad.slnx"];
    private static readonly string[] RepositoryFiles =
    [.. CentralFiles, "LICENSE", NuGetConfigFile, ProducerRelativePath];

    internal string Root { get; } = Path.Combine(Path.GetFullPath(Path.GetTempPath()), FixturePrefix + Guid.NewGuid().ToString(GuidFormat));
    internal string ProjectPath => Path.Combine(Root, ProjectRelativePath.Replace('/', Path.DirectorySeparatorChar));
    internal string OutputDirectory => Path.Combine(Path.GetDirectoryName(ProjectPath)!, "bin", "Release", TargetFramework);
    internal string DllPath => Path.Combine(OutputDirectory, TestAssemblyName + ".dll");
    internal string PdbPath => Path.Combine(OutputDirectory, TestAssemblyName + ".pdb");
    internal string TUnitPropsPath { get; private set; } = string.Empty;
    internal string TUnitSourcePath { get; private set; } = string.Empty;
    internal IOptions<TestExecutionOptions> ExecutionOptions { get; } = ProductionSourceManifestProcess.CaptureExecutionOptions();

    internal async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Root);
        foreach (var relativePath in RepositoryFiles)
        {
            await CopyRepositoryFileAsync(relativePath, cancellationToken);
        }
        await File.WriteAllTextAsync(Path.Combine(Root, SourceRelativePath.Replace('/', Path.DirectorySeparatorChar)), OwnedSource,
            Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(ProjectPath, ProjectTemplate, Encoding.UTF8, cancellationToken);
        var package = ResolvePinnedTUnitPackage();
        TUnitPropsPath = package.PropsPath;
        TUnitSourcePath = package.SourcePath;
    }

    internal async Task WriteCounterfeitAsync(CancellationToken cancellationToken)
        => await File.WriteAllTextAsync(Path.Combine(Root, CounterfeitRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            CounterfeitProps, Encoding.UTF8, cancellationToken);

    internal static string[] CentralRelativePaths => [.. CentralFiles.Select(path => path.Replace('/', Path.DirectorySeparatorChar)),
        ProjectRelativePath.Replace('/', Path.DirectorySeparatorChar)];

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
        return ValueTask.CompletedTask;
    }

    private async Task CopyRepositoryFileAsync(string relativePath, CancellationToken cancellationToken)
    {
        var source = Path.Combine(ProductionSourceManifestProcess.RepositoryRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        var destination = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var input = File.OpenRead(source);
        await using var output = File.Create(destination);
        await input.CopyToAsync(output, cancellationToken);
    }

    private static (string PropsPath, string SourcePath) ResolvePinnedTUnitPackage()
    {
        var assets = Path.Combine(ProductionSourceManifestProcess.RepositoryRoot, ProjectAssetsPath);
        using var stream = File.OpenRead(assets);
        using var document = JsonDocument.Parse(stream);
        var central = XDocument.Load(Path.Combine(ProductionSourceManifestProcess.RepositoryRoot, CentralPackageFile));
        var version = central.Descendants(PackageVersionElement)
            .Single(item => string.Equals((string?)item.Attribute(PackageIncludeAttribute), PinnedTestPackage, StringComparison.Ordinal))
            .Attribute(PackageVersionAttribute)?.Value ?? throw new InvalidDataException(MissingTUnitPin);
        foreach (var folder in document.RootElement.GetProperty(PackageFoldersProperty).EnumerateObject())
        {
            var directory = Path.Combine(folder.Name, TUnitPackageFolder, version, BuildTransitiveDirectory, TargetFramework);
            var props = Path.Combine(directory, TUnitPropsFile);
            var source = Path.Combine(directory, TUnitSourceFile);
            if (File.Exists(props) && File.Exists(source))
            {
                return (Path.GetFullPath(props), Path.GetFullPath(source));
            }
        }
        throw new FileNotFoundException(MissingPinnedPackage);
    }
}
