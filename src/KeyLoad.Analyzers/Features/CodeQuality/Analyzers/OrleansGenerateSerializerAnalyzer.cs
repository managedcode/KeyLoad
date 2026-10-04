using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KeyLoad.Analyzers.Features.CodeQuality;

/// <summary>Requires source-generated serialization for KeyLoad Orleans transport and state types.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OrleansGenerateSerializerAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Stable identifier for this analyzer's diagnostic.</summary>
    public const string DiagnosticId = "KLD0023";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        CodeQualityDiagnosticText.OrleansSerializerTitle,
        CodeQualityDiagnosticText.OrleansSerializerMessage,
        CodeQualityDiagnosticCategories.Reliability,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: CodeQualityDiagnosticText.OrleansSerializerDescription);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        System.ArgumentNullException.ThrowIfNull(context);
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationStartAnalysisContext context)
    {
        if (!CodeQualityAssemblyNames.IsProduction(context.Compilation.AssemblyName) ||
            context.Compilation.GetTypeByMetadataName(
                OrleansMetadataNames.GenerateSerializerAttribute) is not { } generateSerializerAttribute)
        {
            return;
        }

        var missingSerializers = new ConcurrentDictionary<INamedTypeSymbol, byte>(
            SymbolEqualityComparer.Default);
        var analysis = new OrleansSerializerAnalysis(
            context.Compilation,
            generateSerializerAttribute,
            context.Compilation.GetTypeByMetadataName(OrleansMetadataNames.IdAttribute),
            context.Compilation.GetTypeByMetadataName(OrleansMetadataNames.GrainInterface),
            context.Compilation.GetTypeByMetadataName(OrleansMetadataNames.PersistentState),
            context.Compilation.GetTypeByMetadataName(OrleansMetadataNames.TransactionalState),
            missingSerializers);

        context.RegisterSymbolAction(analysis.AnalyzeNamedType, SymbolKind.NamedType);
        context.RegisterCompilationEndAction(endContext =>
            ReportMissingSerializers(endContext, missingSerializers));
    }

    private static void ReportMissingSerializers(
        CompilationAnalysisContext context,
        ConcurrentDictionary<INamedTypeSymbol, byte> missingSerializers)
    {
        foreach (var type in missingSerializers.Keys.OrderBy(
                     static type => type.ToDisplayString(),
                     StringComparer.Ordinal))
        {
            var location = type.Locations.FirstOrDefault(static candidate => candidate.IsInSource);
            if (location is null)
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                location,
                type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
        }
    }
}
