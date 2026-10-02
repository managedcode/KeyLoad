using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KeyLoad.Analyzers.Features.CodeQuality;

/// <summary>Reports inline string literals used as machine keys.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LiteralMachineKeyAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Stable identifier for this analyzer's diagnostic.</summary>
    public const string DiagnosticId = "KLD0001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        CodeQualityDiagnosticText.LiteralMachineKeyTitle,
        CodeQualityDiagnosticText.LiteralMachineKeyMessage,
        CodeQualityDiagnosticCategories.Design,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: CodeQualityDiagnosticText.LiteralMachineKeyDescription);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        System.ArgumentNullException.ThrowIfNull(context);
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeStringLiteral, SyntaxKind.StringLiteralExpression);
    }

    private static void AnalyzeStringLiteral(SyntaxNodeAnalysisContext context)
    {
        var literal = (LiteralExpressionSyntax)context.Node;
        if (literal.Token.ValueText.Length == 0 ||
            !MachineKeyLiteralClassifier.IsMachineKey(context, literal))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, literal.GetLocation(), literal.Token.ValueText));
    }
}
