using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace KeyLoad.Analyzers.CodeQuality;

internal static class UntypedUseAnalysis
{
    internal static void Analyze(OperationAnalysisContext context, UntypedTypeFacts facts, DiagnosticDescriptor rule)
    {
        var operation = context.Operation;
        if (!UntypedOperationSite.IsRuntime(operation) || UntypedSiteReason.Present(operation.Syntax, rule.Id))
        {
            return;
        }
        var type = operation switch
        {
            IInvocationOperation invocation => invocation.TargetMethod.ReturnType,
            IPropertyReferenceOperation property => property.Property.Type,
            IFieldReferenceOperation field => field.Field.Type,
            _ => operation.Type
        };
        if (facts.Contains(type, context.CancellationToken)
            || operation is IInvocationOperation call && HasErasedInput(call, facts, context.CancellationToken))
        {
            context.ReportDiagnostic(Diagnostic.Create(rule, operation.Syntax.GetLocation(), Kind(operation),
                Name(operation)));
        }
    }

    private static bool HasErasedInput(IInvocationOperation call, UntypedTypeFacts facts,
        System.Threading.CancellationToken cancellationToken)
    {
        if (facts.Contains(call.Instance?.Type, cancellationToken)
            || call.TargetMethod.ReducedFrom is not null
                && facts.Contains(call.TargetMethod.ReceiverType, cancellationToken))
        {
            return true;
        }
        foreach (var parameter in call.TargetMethod.Parameters)
        {
            if (facts.Contains(parameter.Type, cancellationToken))
            {
                return true;
            }
        }
        return false;
    }

    private static string Kind(IOperation operation) => operation switch
    {
        IInvocationOperation or IDynamicInvocationOperation => UntypedContractPolicy.Invocation,
        IPropertyReferenceOperation or IFieldReferenceOperation or IDynamicMemberReferenceOperation
            => UntypedContractPolicy.Member,
        _ => UntypedContractPolicy.Creation
    };

    private static string Name(IOperation operation) => operation switch
    {
        IInvocationOperation invocation => invocation.TargetMethod.ToDisplayString(),
        IPropertyReferenceOperation property => property.Property.ToDisplayString(),
        IFieldReferenceOperation field => field.Field.ToDisplayString(),
        _ => operation.Type?.ToDisplayString() ?? operation.Kind.ToString()
    };
}
