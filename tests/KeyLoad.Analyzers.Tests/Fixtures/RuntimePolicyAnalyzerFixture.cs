using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

internal static class RuntimePolicyAnalyzerFixture
{
    internal const string SourcePath = "/repo/src/KeyLoad/Policies/Subject.cs";
    private const string CatalogName = "KeyLoad.Analyzers.Features.CodeQuality.ImplementationTermCatalog";
    private const string PlatformAssemblies = "TRUSTED_PLATFORM_ASSEMBLIES";
    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(CreateReferences);

    internal static string Word(string name) => (string)typeof(UnauthorizedImplementationTermAnalyzer).Assembly
        .GetType(CatalogName, throwOnError: true)!.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

    internal static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(DiagnosticAnalyzer analyzer,
        string assemblyName, bool executable, params (string Path, string Text)[] sources)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var trees = sources.Select(source => CSharpSyntaxTree.ParseText(source.Text,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14), source.Path));
        var references = References.Value.Where(reference =>
            Path.GetFileNameWithoutExtension(reference.Display) != assemblyName);
        var compilation = CSharpCompilation.Create(assemblyName, trees, references,
            new CSharpCompilationOptions(executable ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics(token).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString()).ToArray();
        await Assert.That(errors).IsEmpty();
        return await compilation.WithAnalyzers([analyzer]).GetAnalyzerDiagnosticsAsync(token);
    }

    internal static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(DiagnosticAnalyzer analyzer, string source,
        string path = SourcePath) => AnalyzeAsync(analyzer, "PolicyChecks", false, (path, source));

    private static ImmutableArray<MetadataReference> CreateReferences()
    {
        var root = SiteAnalyzerCoverageXmlBuilder.FindRepositoryRoot();
        var output = Path.Combine(root, "build", "KeyLoad.AppHost", "bin", "Debug", "net10.0");
        var platform = (string)AppContext.GetData(PlatformAssemblies)!;
        return Directory.EnumerateFiles(output, "*.dll").Concat(platform.Split(Path.PathSeparator))
            .DistinctBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToImmutableArray();
    }
}
