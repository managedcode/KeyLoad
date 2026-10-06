using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

internal static class AnalyzerFixture
{
    internal const string OrleansAssembly = "KeyLoad.Orleans";
    internal const string ServerAssembly = "KeyLoad.Server";
    internal const string CoreAssembly = "KeyLoad.Core";
    internal const string CoreTestAssembly = "KeyLoad.Core.Tests";
    internal const string UnitTestAssembly = "KeyLoad.UnitTests";
    internal const string ExternalAssembly = "External.Contracts";
    internal const string ProgramPath = "/repo/src/KeyLoad.Server/Program.cs";
    internal const string ServerEndpointsPath = "/repo/src/KeyLoad.Server/ApiEndpoints.cs";
    internal const string ServerHostingPath = "/repo/src/KeyLoad.Server/Hosting.cs";
    internal const string ContractPath = "/repo/src/KeyLoad.Orleans/Features/Sample/Contracts/Dto.cs";
    internal const string OrleansModelsPath = "/repo/src/KeyLoad.Orleans/Features/Sample/Models.cs";
    internal const string GeneratedPath = "/repo/src/KeyLoad.Orleans/Features/Sample/Generated.g.cs";
    private const string SupportPath = "/repo/src/Support.cs";
    private const string PlatformAssembliesName = "TRUSTED_PLATFORM_ASSEMBLIES";
    private const string MissingPlatformAssemblies = "Trusted platform assemblies are unavailable.";

    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(CreateReferences);

    // AC-CQ-004: every semantic fixture must compile against the actual Orleans package.
    internal static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        DiagnosticAnalyzer analyzer,
        string source,
        string assemblyName = OrleansAssembly,
        string path = ContractPath,
        bool executable = false,
        string? additionalSource = null)
    {
        var tree = CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
            path);
        SyntaxTree[] trees = additionalSource is null
            ? [tree]
            : [tree, CSharpSyntaxTree.ParseText(additionalSource,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
                SupportPath)];
        var compilation = CSharpCompilation.Create(
            assemblyName,
            trees,
            References.Value,
            new CSharpCompilationOptions(executable
                ? OutputKind.ConsoleApplication
                : OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(static diagnostic => diagnostic.ToString())
            .ToArray();
        await Assert.That(errors).IsEmpty();
        return await compilation.WithAnalyzers([analyzer]).GetAnalyzerDiagnosticsAsync();
    }

    internal static async Task AssertFindingAsync(
        ImmutableArray<Diagnostic> diagnostics,
        string id,
        DiagnosticSeverity severity,
        string source,
        string marker,
        string path = ContractPath)
    {
        await Assert.That(diagnostics).HasSingleItem();
        var finding = diagnostics[0];
        await Assert.That(finding.Id).IsEqualTo(id);
        await Assert.That(finding.Severity).IsEqualTo(severity);
        await Assert.That(finding.Location.IsInSource).IsTrue();
        await Assert.That(finding.Location.SourceTree?.FilePath).IsEqualTo(path);
        var position = source.IndexOf(marker, StringComparison.Ordinal);
        await Assert.That(position >= 0).IsTrue();
        var expectedLine = source[..position].Count(static character => character == '\n');
        await Assert.That(finding.Location.GetLineSpan().StartLinePosition.Line)
            .IsEqualTo(expectedLine);
    }

    private static ImmutableArray<MetadataReference> CreateReferences()
    {
        var platformAssemblies = AppContext.GetData(PlatformAssembliesName) as string ??
            throw new InvalidOperationException(MissingPlatformAssemblies);
        var paths = platformAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat([
                typeof(Orleans.IGrain).Assembly.Location,
                typeof(Orleans.Grain).Assembly.Location,
                typeof(Orleans.GenerateSerializerAttribute).Assembly.Location,
                typeof(Orleans.Runtime.IPersistentState<>).Assembly.Location,
                typeof(Microsoft.AspNetCore.Builder.WebApplication).Assembly.Location,
                typeof(KeyLoad.ConfigurationOptionsAttribute).Assembly.Location,
                typeof(KeyLoad.Diagnostics.Features.ResourceExecution.DatabasePhaseBank).Assembly.Location,
                typeof(Microsoft.Extensions.Options.IOptions<>).Assembly.Location,
                typeof(Microsoft.Extensions.Configuration.IConfiguration).Assembly.Location,
                typeof(Microsoft.Extensions.Configuration.ConfigurationBinder).Assembly.Location,
                typeof(Microsoft.Extensions.DependencyInjection.OptionsServiceCollectionExtensions).Assembly.Location
            ])
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return paths.Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
    }
}
