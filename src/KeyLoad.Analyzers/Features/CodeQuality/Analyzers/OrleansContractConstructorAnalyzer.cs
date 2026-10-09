using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KeyLoad.Analyzers.Features.CodeQuality;

/// <summary>Reports instance constructors on KeyLoad Orleans contract DTOs.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OrleansContractConstructorAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Stable identifier for this analyzer's diagnostic.</summary>
    public const string DiagnosticId = "KLD0021";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        CodeQualityDiagnosticText.OrleansConstructorTitle,
        CodeQualityDiagnosticText.OrleansConstructorMessage,
        CodeQualityDiagnosticCategories.Reliability,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: CodeQualityDiagnosticText.OrleansConstructorDescription);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        System.ArgumentNullException.ThrowIfNull(context);
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            AnalyzeConstructor,
            SyntaxKind.ConstructorDeclaration);
    }

    private static void AnalyzeConstructor(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not ConstructorDeclarationSyntax constructor ||
            context.ContainingSymbol?.ContainingType is not { } containingType ||
            !IsProductionAssembly(context.SemanticModel.Compilation.AssemblyName) ||
            (!HasGenerateSerializer(containingType) &&
             !IsSharedOrleansContract(
                 context.SemanticModel.Compilation.AssemblyName,
                 constructor.SyntaxTree.FilePath)))
        {
            return;
        }

        if (IsNativeException(containingType, context))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            constructor.Identifier.GetLocation(),
            containingType.Name));
    }

    private static bool IsNativeException(INamedTypeSymbol type, SyntaxNodeAnalysisContext context)
    {
        var exceptionType = context.SemanticModel.Compilation.GetTypeByMetadataName(
            OrleansMetadataNames.SystemException);
        if (exceptionType is null)
        {
            return false;
        }

        for (var current = type; current is not null; current = current.BaseType)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (SymbolEqualityComparer.Default.Equals(current, exceptionType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasGenerateSerializer(INamedTypeSymbol type) =>
        type.GetAttributes().Any(static attribute =>
            string.Equals(
                attribute.AttributeClass?.ToDisplayString(),
                OrleansMetadataNames.GenerateSerializerAttribute,
                StringComparison.Ordinal));

    private static bool IsSharedOrleansContract(
        string? assemblyName,
        string filePath)
    {
        if (!string.Equals(
                assemblyName,
                CodeQualityAssemblyNames.OrleansContracts,
                StringComparison.Ordinal))
        {
            return false;
        }

        var normalizedPath = filePath.Replace('\\', '/');
        return normalizedPath.Contains(CodeQualitySourceNames.ContractsDirectorySegment, StringComparison.Ordinal);
    }

    private static bool IsProductionAssembly(string? assemblyName) =>
        CodeQualityAssemblyNames.IsProduction(assemblyName);
}
