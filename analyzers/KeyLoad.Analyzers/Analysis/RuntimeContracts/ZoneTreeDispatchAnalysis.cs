using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class ZoneTreeDispatchAnalysis
{
    internal static bool ContainsAccess(IOperation operation, OperationAnalysisContext context,
        HashSet<IMethodSymbol> visited)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        var method = operation switch
        {
            IInvocationOperation invocation => invocation.TargetMethod,
            IMethodReferenceOperation reference => reference.Method,
            _ => null
        };
        if (ZoneTreeAccessPolicy.Execution(method) || operation is IObjectCreationOperation creation &&
            ZoneTreeAccessPolicy.Execution(creation.Constructor) || operation is IPropertyReferenceOperation property &&
            ZoneTreeAccessPolicy.Execution(property.Property))
        {
            return true;
        }
        if (method is not null && visited.Add(method.OriginalDefinition) && MethodBodyUsesAccess(method, context, visited))
        {
            return true;
        }
        foreach (var child in operation.ChildOperations)
        {
            if (ContainsAccess(child, context, visited))
            {
                return true;
            }
        }
        return false;
    }

    internal static bool RuntimeResource(IOperation operation)
    {
        if (ZoneTreeAccessPolicy.Execution(operation.Type))
        {
            return true;
        }
        foreach (var child in operation.ChildOperations)
        {
            if (RuntimeResource(child))
            {
                return true;
            }
        }
        return false;
    }

    private static bool MethodBodyUsesAccess(IMethodSymbol method, OperationAnalysisContext context,
        HashSet<IMethodSymbol> visited)
    {
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            var syntax = reference.GetSyntax(context.CancellationToken);
            var model = context.Compilation.GetSemanticModel(reference.SyntaxTree);
            if (model.GetOperation(syntax, context.CancellationToken) is { } body && ContainsAccess(body, context, visited))
            {
                return true;
            }
        }
        return false;
    }
}
