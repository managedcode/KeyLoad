using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KeyLoad.Analyzers.Features.CodeQuality;

/// <summary>Requires positive version attributes on KeyLoad Orleans grain interfaces.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GrainInterfaceVersionAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Stable identifier for this analyzer's diagnostic.</summary>
    public const string DiagnosticId = "KLD0014";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        CodeQualityDiagnosticText.GrainVersionTitle,
        CodeQualityDiagnosticText.GrainVersionMessage,
        CodeQualityDiagnosticCategories.Reliability,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: CodeQualityDiagnosticText.GrainVersionDescription);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        System.ArgumentNullException.ThrowIfNull(context);
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static startContext =>
        {
            if (!string.Equals(startContext.Compilation.AssemblyName,
                    CodeQualityAssemblyNames.OrleansContracts,
                    StringComparison.Ordinal) ||
                startContext.Compilation.GetTypeByMetadataName(OrleansMetadataNames.GrainInterface) is not { } grainInterface)
            {
                return;
            }

            startContext.RegisterSymbolAction(
                symbolContext => AnalyzeNamedType(symbolContext, grainInterface),
                SymbolKind.NamedType);
        });
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context, INamedTypeSymbol grainInterface)
    {
        if (context.Symbol is not INamedTypeSymbol type ||
            type.TypeKind != TypeKind.Interface ||
            string.Equals(type.ContainingNamespace.ToDisplayString(),
                CodeQualitySourceNames.OrleansNamespace,
                StringComparison.Ordinal) ||
            SymbolEqualityComparer.Default.Equals(type, grainInterface) ||
            !type.AllInterfaces.Any(candidate =>
                SymbolEqualityComparer.Default.Equals(candidate, grainInterface)) ||
            HasPositiveVersion(type))
        {
            return;
        }

        var location = type.Locations.FirstOrDefault(static candidate => candidate.IsInSource);
        if (location is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, location, type.Name));
        }
    }

    private static bool HasPositiveVersion(INamedTypeSymbol type)
    {
        var attribute = type.GetAttributes().FirstOrDefault(static candidate =>
            string.Equals(candidate.AttributeClass?.ToDisplayString(),
                OrleansMetadataNames.VersionAttribute,
                StringComparison.Ordinal));
        return attribute is { ConstructorArguments.Length: 1 } &&
               attribute.ConstructorArguments[0].Value is ushort version &&
               version > 0;
    }
}
