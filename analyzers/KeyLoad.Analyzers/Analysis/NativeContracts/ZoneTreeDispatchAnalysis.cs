using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class ZoneTreeDispatchAnalysis
{
    internal static bool ContainsNativeAccess(IOperation operation, OperationAnalysisContext context,
        HashSet<IMethodSymbol> visited)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        var method = operation switch
        {
            IInvocationOperation invocation => invocation.TargetMethod,
            IMethodReferenceOperation reference => reference.Method,
            _ => null
        };
        if (ZoneTreeAccessPolicy.Native(method) || operation is IObjectCreationOperation creation &&
            ZoneTreeAccessPolicy.Native(creation.Constructor) || operation is IPropertyReferenceOperation property &&
            ZoneTreeAccessPolicy.Native(property.Property))
        {
            return true;
        }
        if (method is not null && visited.Add(method.OriginalDefinition) && MethodBodyUsesNativeAccess(method, context, visited))
        {
            return true;
        }
        foreach (var child in operation.ChildOperations)
        {
            if (ContainsNativeAccess(child, context, visited))
            {
                return true;
            }
        }
        return false;
    }

    internal static bool NativeResource(IOperation operation)
    {
        if (ZoneTreeAccessPolicy.Native(operation.Type))
        {
            return true;
        }
        foreach (var child in operation.ChildOperations)
        {
            if (NativeResource(child))
            {
                return true;
            }
        }
        return false;
    }

    private static bool MethodBodyUsesNativeAccess(IMethodSymbol method, OperationAnalysisContext context,
        HashSet<IMethodSymbol> visited)
    {
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            var syntax = reference.GetSyntax(context.CancellationToken);
            var model = context.Compilation.GetSemanticModel(reference.SyntaxTree);
            if (model.GetOperation(syntax, context.CancellationToken) is { } body && ContainsNativeAccess(body, context, visited))
            {
                return true;
            }
        }
        return false;
    }
}
