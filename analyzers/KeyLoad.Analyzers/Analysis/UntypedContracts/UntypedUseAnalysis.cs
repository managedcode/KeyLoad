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
            || operation is IInvocationOperation call && HasErasedInput(call, facts, context.CancellationToken)
            || operation is IObjectCreationOperation creation
                && HasErasedParameters(creation.Constructor, facts, context.CancellationToken))
        {
            context.ReportDiagnostic(Diagnostic.Create(rule, operation.Syntax.GetLocation(), Kind(operation),
                Name(operation)) /* KLD0041: Roslyn requires object message arguments for this use kind and the resolved API member identity. */);
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
        return HasErasedParameters(call.TargetMethod, facts, cancellationToken);
    }

    private static bool HasErasedParameters(IMethodSymbol? method, UntypedTypeFacts facts,
        System.Threading.CancellationToken cancellationToken)
    {
        if (method is null)
        {
            return false;
        }
        foreach (var parameter in method.Parameters)
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
        IObjectCreationOperation creation when creation.Constructor is { } constructor => constructor.ToDisplayString(),
        _ => operation.Type?.ToDisplayString() ?? operation.Kind.ToString()
    };
}
