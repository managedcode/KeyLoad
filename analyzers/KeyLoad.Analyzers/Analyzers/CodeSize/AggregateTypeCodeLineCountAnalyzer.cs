using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KeyLoad.Analyzers.CodeQuality;

/// <summary>Reports named types whose nongenerated declarations exceed 200 code lines.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AggregateTypeCodeLineCountAnalyzer : DiagnosticAnalyzer
{
    private const int MaximumTypeCodeLines = 200;
    private static readonly ImmutableArray<DiagnosticDescriptor> Rules =
        [NumericQualityRuleDescriptors.AggregateTypeCodeLines];
    private static readonly SyntaxKind[] TypeDeclarationKinds =
    [
        SyntaxKind.ClassDeclaration,
        SyntaxKind.StructDeclaration,
        SyntaxKind.InterfaceDeclaration,
        SyntaxKind.RecordDeclaration,
        SyntaxKind.RecordStructDeclaration,
        SyntaxKind.EnumDeclaration,
        SyntaxKind.DelegateDeclaration
    ];

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
        context.RegisterCompilationStartAction(RegisterCompilationAnalysis);
    }

    private static void RegisterCompilationAnalysis(CompilationStartAnalysisContext context)
    {
        var measurements = new AggregateTypeCodeLineAccumulator();
        context.RegisterSyntaxNodeAction(
            nodeContext => MeasureDeclaration(nodeContext, measurements),
            TypeDeclarationKinds);
        context.RegisterCompilationEndAction(endContext => ReportMeasurements(endContext, measurements));
    }

    private static void MeasureDeclaration(
        SyntaxNodeAnalysisContext context,
        AggregateTypeCodeLineAccumulator measurements)
    {
        if (context.SemanticModel.GetDeclaredSymbol(
                context.Node, context.CancellationToken) is not INamedTypeSymbol type ||
            type.IsImplicitlyDeclared)
        {
            return;
        }

        var count = NumericCodeLineCounter.Count(
            context.Node, context.Node.Span, context.CancellationToken);
        measurements.Add(type, count, context.Node.GetLocation());
    }

    private static void ReportMeasurements(
        CompilationAnalysisContext context,
        AggregateTypeCodeLineAccumulator measurements)
    {
        foreach (var measurement in measurements.Snapshot()
                     .OrderBy(static item => item.Location.SourceTree?.FilePath, StringComparer.Ordinal)
                     .ThenBy(static item => item.Location.SourceSpan.Start))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (measurement.CodeLines > MaximumTypeCodeLines)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rules[0], measurement.Location, measurement.Type.Name, measurement.CodeLines));
            }
        }
    }
}
