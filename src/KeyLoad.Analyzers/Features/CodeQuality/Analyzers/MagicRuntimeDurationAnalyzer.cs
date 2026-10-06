using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KeyLoad.Analyzers.Features.CodeQuality;

/// <summary>Requires named constants for every explicit runtime numeric literal.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MagicRuntimeDurationAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Stable compiler and SARIF diagnostic identifier.</summary>
    public const string DiagnosticId = "KLD0035";

    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId,
        CodeQualityDiagnosticText.MagicRuntimeDurationTitle,
        CodeQualityDiagnosticText.MagicRuntimeDurationMessage,
        CodeQualityDiagnosticCategories.Design, DiagnosticSeverity.Error, isEnabledByDefault: true,
        description: CodeQualityDiagnosticText.MagicRuntimeDurationDescription);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeLiteral, SyntaxKind.NumericLiteralExpression);
    }

    private static void AnalyzeLiteral(SyntaxNodeAnalysisContext context)
    {
        if (!CodeQualityAssemblyNames.IsProduction(context.Compilation.AssemblyName) ||
            context.Node is not LiteralExpressionSyntax literal ||
            LiteralDeclarationOwnership.IsNamedConstant(literal) ||
            LiteralDeclarationOwnership.IsNumericMetadata(literal) ||
            LiteralDeclarationOwnership.IsOptionsDefault(context))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, literal.GetLocation(), literal.ToString()));
    }
}
