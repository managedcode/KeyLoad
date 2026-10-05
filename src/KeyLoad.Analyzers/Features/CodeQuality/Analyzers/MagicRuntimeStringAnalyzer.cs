using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KeyLoad.Analyzers.Features.CodeQuality;

/// <summary>Requires named identities for runtime string, character and interpolation text literals.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MagicRuntimeStringAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Stable compiler and SARIF diagnostic identifier.</summary>
    public const string DiagnosticId = "KLD0036";

    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId,
        CodeQualityDiagnosticText.MagicRuntimeStringTitle,
        CodeQualityDiagnosticText.MagicRuntimeStringMessage,
        CodeQualityDiagnosticCategories.Design, DiagnosticSeverity.Error, isEnabledByDefault: true,
        description: CodeQualityDiagnosticText.MagicRuntimeStringDescription);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeLiteral, SyntaxKind.StringLiteralExpression,
            SyntaxKind.CharacterLiteralExpression, SyntaxKind.Utf8StringLiteralExpression,
            SyntaxKind.InterpolatedStringText, SyntaxKind.InterpolationFormatClause);
    }

    private static void AnalyzeLiteral(SyntaxNodeAnalysisContext context)
    {
        if (!CodeQualityAssemblyNames.IsProduction(context.Compilation.AssemblyName) ||
            LiteralDeclarationOwnership.IsNamedConstant(context.Node) ||
            LiteralDeclarationOwnership.IsOptionsDefault(context) ||
            context.Node is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression) &&
            literal.Token.ValueText.Length > 0 && MachineKeyLiteralClassifier.IsMachineKey(context, literal))
        {
            return;
        }

        var location = context.Node is InterpolationFormatClauseSyntax format
            ? format.FormatStringToken.GetLocation()
            : context.Node.GetLocation();
        context.ReportDiagnostic(Diagnostic.Create(Rule, location, context.Node.ToString()));
    }
}
