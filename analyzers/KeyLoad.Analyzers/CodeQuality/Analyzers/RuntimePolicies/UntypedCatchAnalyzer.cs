using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KeyLoad.Analyzers.Features.CodeQuality;

/// <summary>Reports catch clauses that do not name an exception type.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UntypedCatchAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Stable identifier for this analyzer's diagnostic.</summary>
    public const string DiagnosticId = "KLD0024";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        CodeQualityDiagnosticText.UntypedCatchTitle,
        CodeQualityDiagnosticText.UntypedCatchMessage,
        CodeQualityDiagnosticCategories.Reliability,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: CodeQualityDiagnosticText.UntypedCatchDescription);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        System.ArgumentNullException.ThrowIfNull(context);
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeCatchClause, SyntaxKind.CatchClause);
    }

    private static void AnalyzeCatchClause(SyntaxNodeAnalysisContext context)
    {
        var clause = (CatchClauseSyntax)context.Node;
        if (clause.Declaration is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, clause.CatchKeyword.GetLocation()));
        }
    }
}
