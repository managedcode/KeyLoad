using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

internal static class NumericAnalyzerFixture
{
    private const string MissingPlatformAssemblies = "Trusted platform assemblies are unavailable.";
    private const string PlatformAssembliesName = "TRUSTED_PLATFORM_ASSEMBLIES";
    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(CreateReferences);

    internal static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        DiagnosticAnalyzer analyzer,
        params (string Path, string Text)[] sources) =>
        AnalyzeSourcesAsync(analyzer, "NumericAnalyzerFixture", false, sources);

    internal static Task<ImmutableArray<Diagnostic>> AnalyzeWithUnsafeAsync(
        DiagnosticAnalyzer analyzer,
        params (string Path, string Text)[] sources) =>
        AnalyzeSourcesAsync(analyzer, "NumericAnalyzerFixture", true, sources);

    internal static Task<ImmutableArray<Diagnostic>> AnalyzeSourcesAsync(
        DiagnosticAnalyzer analyzer,
        string assemblyName,
        params (string Path, string Text)[] sources)
        => AnalyzeSourcesAsync(analyzer, assemblyName, false, sources);

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeSourcesAsync(
        DiagnosticAnalyzer analyzer,
        string assemblyName,
        bool allowUnsafe,
        params (string Path, string Text)[] sources)
    {
        var trees = sources.Select(static source => CSharpSyntaxTree.ParseText(
            source.Text,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
            source.Path));
        var compilation = CSharpCompilation.Create(
            assemblyName,
            trees,
            References.Value,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary, allowUnsafe: allowUnsafe));
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(static diagnostic => diagnostic.ToString())
            .ToArray();
        await Assert.That(errors).IsEmpty();
        return await compilation.WithAnalyzers([analyzer]).GetAnalyzerDiagnosticsAsync();
    }

    internal static async Task AssertSingleFindingAsync(
        ImmutableArray<Diagnostic> findings,
        string id,
        DiagnosticSeverity severity,
        string? path = null,
        int? line = null,
        int? column = null,
        TextSpan? span = null)
    {
        await Assert.That(findings).HasSingleItem();
        await Assert.That(findings[0].Id).IsEqualTo(id);
        await Assert.That(findings[0].Severity).IsEqualTo(severity);
        await Assert.That(findings[0].Location.IsInSource).IsTrue();
        if (path is not null)
        {
            await Assert.That(findings[0].Location.SourceTree?.FilePath).IsEqualTo(path);
        }

        if (line is not null)
        {
            await Assert.That(findings[0].Location.GetLineSpan().StartLinePosition.Line)
                .IsEqualTo(line.Value);
        }

        if (column is not null)
        {
            await Assert.That(findings[0].Location.GetLineSpan().StartLinePosition.Character)
                .IsEqualTo(column.Value);
        }

        if (span is not null)
        {
            await Assert.That(findings[0].Location.SourceSpan).IsEqualTo(span.Value);
        }
    }

    private static ImmutableArray<MetadataReference> CreateReferences()
    {
        var platformAssemblies = AppContext.GetData(PlatformAssembliesName) as string ??
            throw new InvalidOperationException(MissingPlatformAssemblies);
        var paths = platformAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(AppDomain.CurrentDomain.GetAssemblies()
                .Where(static assembly => !assembly.IsDynamic)
                .Select(static assembly => assembly.Location)
                .Where(static path => !string.IsNullOrEmpty(path)))
            .Append(typeof(Assembly).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return paths
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
    }
}
