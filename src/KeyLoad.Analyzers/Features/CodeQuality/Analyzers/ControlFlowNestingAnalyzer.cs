using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KeyLoad.Analyzers.Features.CodeQuality;

/// <summary>Reports executable units with control-flow nesting greater than three.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ControlFlowNestingAnalyzer : DiagnosticAnalyzer
{
    private const int MaximumControlFlowDepth = 3;
    private static readonly ImmutableArray<DiagnosticDescriptor> Rules =
        [NumericQualityRuleDescriptors.ControlFlowNesting];

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

        var body = ExecutableUnitSyntax.GetBody(unit);
        if (body is null)
        {
            return;
        }

        var depth = ControlFlowNestingWalker.Measure(
            unit, body, context.CancellationToken);
        if (depth > MaximumControlFlowDepth)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rules[0], unit.GetLocation(), depth));
        }
    }
}
