using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace KeyLoad.Analyzers.CodeQuality;

/// <summary>Requires explicit operation results instead of returning null.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NullReturnAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Stable compiler and SARIF diagnostic identifier.</summary>
    public const string DiagnosticId = "KLD0040";
    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId,
        CodeQualityDiagnosticText.NullReturnTitle, CodeQualityDiagnosticText.NullReturnMessage,
        CodeQualityDiagnosticCategories.Reliability, DiagnosticSeverity.Error,
        isEnabledByDefault: true, description: CodeQualityDiagnosticText.NullReturnDescription);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
        {
            throw new System.ArgumentNullException(nameof(context));
        }
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeReturn, Microsoft.CodeAnalysis.OperationKind.Return);
    }

    private static void AnalyzeReturn(OperationAnalysisContext context)
    {
        var operation = (IReturnOperation)context.Operation;
        if (!ExternalNullReturnContract.Allows(operation, context))
        {
            AnalyzeValue(operation.ReturnedValue, context);
        }
    }

    private static void AnalyzeValue(IOperation? value, OperationAnalysisContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (value is null)
        {
            return;
        }
        if (value.ConstantValue is { HasValue: true, Value: null } ||
            value is IDefaultValueOperation { Type.OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, value.Syntax.GetLocation()));
            return;
        }
        switch (value)
        {
            case IConversionOperation conversion:
                AnalyzeValue(conversion.Operand, context);
                break;
            case IParenthesizedOperation parenthesized:
                AnalyzeValue(parenthesized.Operand, context);
                break;
            case IConditionalOperation conditional:
                AnalyzeValue(conditional.WhenTrue, context);
                AnalyzeValue(conditional.WhenFalse, context);
                break;
            case ISwitchExpressionOperation expression:
                foreach (var arm in expression.Arms)
                {
                    AnalyzeValue(arm.Value, context);
                }
                break;
            case ICoalesceOperation coalesce:
                AnalyzeValue(coalesce.WhenNull, context);
                break;
        }
    }
}
