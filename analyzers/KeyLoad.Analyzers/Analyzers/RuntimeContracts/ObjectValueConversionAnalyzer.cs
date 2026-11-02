using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KeyLoad.Analyzers.CodeQuality;

/// <summary>Reviews value boxing and object casts independently of erased declarations and calls.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ObjectValueConversionAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Stable identifier for boxing and conversions from object.</summary>
    public const string DiagnosticId = UntypedContractPolicy.ConversionId;
    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, UntypedContractPolicy.ConversionTitle,
        UntypedContractPolicy.ConversionMessage, CodeQualityDiagnosticCategories.Architecture,
        DiagnosticSeverity.Warning, isEnabledByDefault: true, description: UntypedContractPolicy.ConversionDescription);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
        {
            throw new System.ArgumentNullException(nameof(context));
        }
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(operation => ObjectConversionAnalysis.Analyze(operation, Rule),
            Microsoft.CodeAnalysis.OperationKind.Conversion);
        context.RegisterOperationAction(operation => ObjectConversionAnalysis.AnalyzePattern(operation, Rule),
            Microsoft.CodeAnalysis.OperationKind.IsPattern);
    }
}
