using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class HardcodedDurationPolicy
{
    internal static bool IsDurationMethod(Compilation compilation, IMethodSymbol method)
    {
        var type = method.ContainingType;
        if (MagicRuntimeOperations.IsNativeType(compilation, type, MagicRuntimeMetadataNames.TimeSpan))
        {
            return method.MethodKind == MethodKind.Constructor ||
                method.IsStatic && method.Name.StartsWith(MagicRuntimeMetadataNames.FromPrefix, StringComparison.Ordinal);
        }

        if (MagicRuntimeOperations.IsNativeType(compilation, type, MagicRuntimeMetadataNames.CancellationSource))
        {
            return method.MethodKind == MethodKind.Constructor || method.Name == MagicRuntimeMetadataNames.CancelAfter;
        }

        return method.Name is MagicRuntimeMetadataNames.Delay or MagicRuntimeMetadataNames.Wait or
                MagicRuntimeMetadataNames.WaitAsync &&
            (MagicRuntimeOperations.IsNativeType(compilation, type, MagicRuntimeMetadataNames.Task) ||
             MagicRuntimeOperations.IsNativeType(compilation, type, MagicRuntimeMetadataNames.GenericTask) ||
             MagicRuntimeOperations.IsNativeType(compilation, type, MagicRuntimeMetadataNames.Semaphore));
    }

    internal static bool IsHardcoded(Compilation compilation, IOperation operation, CancellationToken cancellationToken) =>
        IsHardcoded(compilation, operation, new HashSet<ISymbol>(SymbolEqualityComparer.Default), cancellationToken);

    private static bool IsHardcoded(Compilation compilation, IOperation operation,
        HashSet<ISymbol> visited, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsFixedNativeIdentity(compilation, operation))
        {
            return false;
        }

        if (MagicRuntimeOperations.IsNumeric(operation.Type) && operation.ConstantValue.HasValue)
        {
            return true;
        }

        return operation switch
        {
            IFieldReferenceOperation field when field.Field.IsReadOnly &&
                !HasAuthoredWrites(compilation, field.Field, cancellationToken) =>
                HasHardcodedInitializer(compilation, field.Field, visited, cancellationToken),
            ILocalReferenceOperation local when local.Local.RefKind == RefKind.None &&
                !HasAuthoredWrites(compilation, local.Local, cancellationToken) =>
                HasHardcodedInitializer(compilation, local.Local, visited, cancellationToken),
            IInvocationOperation invocation when IsDurationMethod(compilation, invocation.TargetMethod) =>
                invocation.Arguments.Any(argument => IsHardcoded(compilation, argument.Value, visited, cancellationToken)),
            IObjectCreationOperation { Constructor: { } constructor } creation when IsDurationMethod(compilation, constructor) =>
                creation.Arguments.Any(argument => IsHardcoded(compilation, argument.Value, visited, cancellationToken)),
            IConversionOperation conversion => IsHardcoded(compilation, conversion.Operand, visited, cancellationToken),
            IUnaryOperation unary => IsHardcoded(compilation, unary.Operand, visited, cancellationToken),
            IBinaryOperation binary => IsHardcoded(compilation, binary.LeftOperand, visited, cancellationToken) ||
                IsHardcoded(compilation, binary.RightOperand, visited, cancellationToken),
            IParenthesizedOperation parenthesized => IsHardcoded(compilation, parenthesized.Operand, visited, cancellationToken),
            _ => false
        };
    }

    private static bool HasHardcodedInitializer(Compilation compilation, ISymbol symbol,
        HashSet<ISymbol> visited, CancellationToken cancellationToken)
    {
        if (!visited.Add(symbol))
        {
            return false;
        }

        return symbol.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax(cancellationToken) is VariableDeclaratorSyntax { Initializer.Value: { } value } &&
            compilation.GetSemanticModel(value.SyntaxTree).GetOperation(value, cancellationToken) is { } initializer &&
            IsHardcoded(compilation, initializer, visited, cancellationToken));
    }

    private static bool HasAuthoredWrites(Compilation compilation, ISymbol symbol, CancellationToken cancellationToken)
    {
        var roots = symbol is IFieldSymbol field
            ? field.ContainingType.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax(cancellationToken))
            : symbol.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax(cancellationToken)
                .Ancestors().FirstOrDefault(static node => node is BaseMethodDeclarationSyntax or
                    LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax or AccessorDeclarationSyntax or CompilationUnitSyntax));
        foreach (var root in roots)
        {
            if (root is null)
            {
                return true;
            }

            var model = compilation.GetSemanticModel(root.SyntaxTree);
            foreach (var node in root.DescendantNodes().Where(static node => node is AssignmentExpressionSyntax or
                         PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax or ArgumentSyntax))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = model.GetOperation(node, cancellationToken) switch
                {
                    IAssignmentOperation assignment => assignment.Target,
                    IIncrementOrDecrementOperation increment => increment.Target,
                    IArgumentOperation { Parameter.RefKind: RefKind.Ref or RefKind.Out } argument => argument.Value,
                    _ => null
                };
                if (target is not null && ReferencesSymbol(target, symbol))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ReferencesSymbol(IOperation operation, ISymbol symbol) =>
        operation is IFieldReferenceOperation field && SymbolEqualityComparer.Default.Equals(field.Field, symbol) ||
        operation is ILocalReferenceOperation local && SymbolEqualityComparer.Default.Equals(local.Local, symbol) ||
        operation.ChildOperations.Any(child => ReferencesSymbol(child, symbol));

    private static bool IsFixedNativeIdentity(Compilation compilation, IOperation operation) =>
        operation is IFieldReferenceOperation field &&
        (MagicRuntimeOperations.IsNativeType(compilation, field.Field.ContainingType, MagicRuntimeMetadataNames.TimeSpan) &&
            field.Field.Name is ConfigurationMetadataNames.Zero or ConfigurationMetadataNames.MinValue or ConfigurationMetadataNames.MaxValue ||
         MagicRuntimeOperations.IsNativeType(compilation, field.Field.ContainingType, ConfigurationMetadataNames.Timeout) &&
            field.Field.Name is ConfigurationMetadataNames.Infinite or ConfigurationMetadataNames.InfiniteTimeSpan);
}
