using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace KeyLoad.Analyzers.CodeQuality;

internal static class ExternalNullReturnContract
{
    internal static bool Allows(IReturnOperation operation, OperationAnalysisContext context)
    {
        for (var parent = operation.Parent; parent is not null; parent = parent.Parent)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (parent is IAnonymousFunctionOperation anonymous)
            {
                return Callback(anonymous, context.Compilation);
            }
            if (parent is ILocalFunctionOperation)
            {
                return false;
            }
        }
        var method = context.ContainingSymbol as IMethodSymbol ??
            (context.ContainingSymbol as IPropertySymbol)?.GetMethod;
        return method is not null && (Overrides(method, context.Compilation) || Implements(method, context));
    }

    private static bool Overrides(IMethodSymbol method, Compilation compilation)
    {
        for (var contract = method.OverriddenMethod; contract is not null; contract = contract.OverriddenMethod)
        {
            if (External(contract, compilation) && Nullable(contract.ReturnType, method.IsAsync))
            {
                return true;
            }
        }
        return false;
    }

    private static bool Implements(IMethodSymbol method, OperationAnalysisContext context)
    {
        foreach (var contract in method.ContainingType.AllInterfaces)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            foreach (var member in contract.GetMembers())
            {
                if (External(member, context.Compilation) && Matches(method, member) &&
                    SymbolEqualityComparer.Default.Equals(method.ContainingType.FindImplementationForInterfaceMember(member),
                        method.AssociatedSymbol ?? method))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool Matches(IMethodSymbol method, ISymbol member) => member switch
    {
        IMethodSymbol contract => Nullable(contract.ReturnType, method.IsAsync),
        IPropertySymbol property => method.MethodKind == MethodKind.PropertyGet && Nullable(property.Type),
        _ => false
    };

    private static bool Callback(IAnonymousFunctionOperation anonymous, Compilation compilation)
    {
        IOperation converted = anonymous;
        while (converted.Parent is IConversionOperation or IDelegateCreationOperation)
        {
            converted = converted.Parent;
        }
        return converted.Parent is IArgumentOperation { Parameter: { } parameter } &&
            External(parameter.ContainingSymbol, compilation) &&
            parameter.Type is INamedTypeSymbol { DelegateInvokeMethod: { } invoke } &&
            Nullable(invoke.ReturnType, anonymous.Symbol.IsAsync);
    }

    private static bool External(ISymbol symbol, Compilation compilation) =>
        !SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, compilation.Assembly);

    private static bool Nullable(ITypeSymbol type, bool asynchronous = false)
    {
        if (asynchronous && type is INamedTypeSymbol { TypeArguments.Length: 1 } task)
        {
            type = task.TypeArguments[0];
        }
        return type.IsReferenceType && type.NullableAnnotation != NullableAnnotation.NotAnnotated ||
            type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
    }
}
