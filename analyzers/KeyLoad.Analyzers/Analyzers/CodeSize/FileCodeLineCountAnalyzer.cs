using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KeyLoad.Analyzers.CodeQuality;

/// <summary>Reports C# source files whose code line count exceeds 400.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FileCodeLineCountAnalyzer : DiagnosticAnalyzer
{
    private const int MaximumFileCodeLines = 400;
    private static readonly ImmutableArray<DiagnosticDescriptor> Rules =
        [NumericQualityRuleDescriptors.FileCodeLines];

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => Rules;

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
        {
            throw new System.ArgumentNullException(nameof(context));
        }
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(AnalyzeFile);
    }

    private static void AnalyzeFile(SyntaxTreeAnalysisContext context)
    {
        var root = context.Tree.GetRoot(context.CancellationToken);
        var count = NumericCodeLineCounter.Count(
            root, root.FullSpan, context.CancellationToken);
        if (count > MaximumFileCodeLines)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rules[0], root.GetLocation(), count));
        }
    }
}
