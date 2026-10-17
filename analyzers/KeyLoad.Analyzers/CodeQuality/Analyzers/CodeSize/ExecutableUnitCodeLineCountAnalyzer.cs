using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KeyLoad.Analyzers.Features.CodeQuality;

/// <summary>Reports executable units whose code line count exceeds 64.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExecutableUnitCodeLineCountAnalyzer : DiagnosticAnalyzer
{
    private const int MaximumExecutableUnitCodeLines = 64;
    private static readonly ImmutableArray<DiagnosticDescriptor> Rules =
        [NumericQualityRuleDescriptors.ExecutableUnitCodeLines];

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => Rules;

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeUnit, ExecutableUnitSyntax.Kinds);
    }

    private static void AnalyzeUnit(SyntaxNodeAnalysisContext context)
    {
        var unit = context.Node;
        if (!ExecutableUnitSyntax.IsExecutableUnit(unit))
        {
            return;
        }

        var count = NumericCodeLineCounter.Count(
            unit, unit.Span, context.CancellationToken);
        if (count > MaximumExecutableUnitCodeLines)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rules[0], unit.GetLocation(), count));
        }
    }
}
