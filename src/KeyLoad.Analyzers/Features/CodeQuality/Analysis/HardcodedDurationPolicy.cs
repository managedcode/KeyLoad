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
            IFieldReferenceOperation field when field.Field.IsStatic && field.Field.IsReadOnly =>
                HasHardcodedInitializer(compilation, field.Field, visited, cancellationToken),
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

    private static bool HasHardcodedInitializer(Compilation compilation, IFieldSymbol field,
        HashSet<ISymbol> visited, CancellationToken cancellationToken)
    {
        if (!visited.Add(field))
        {
            return false;
        }

        return field.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax(cancellationToken) is VariableDeclaratorSyntax { Initializer.Value: { } value } &&
            compilation.GetSemanticModel(value.SyntaxTree).GetOperation(value, cancellationToken) is { } initializer &&
            IsHardcoded(compilation, initializer, visited, cancellationToken));
    }

    private static bool IsFixedNativeIdentity(Compilation compilation, IOperation operation) =>
        operation is IFieldReferenceOperation field &&
        (MagicRuntimeOperations.IsNativeType(compilation, field.Field.ContainingType, MagicRuntimeMetadataNames.TimeSpan) &&
            field.Field.Name is ConfigurationMetadataNames.Zero or ConfigurationMetadataNames.MinValue or ConfigurationMetadataNames.MaxValue ||
         MagicRuntimeOperations.IsNativeType(compilation, field.Field.ContainingType, ConfigurationMetadataNames.Timeout) &&
            field.Field.Name is ConfigurationMetadataNames.Infinite or ConfigurationMetadataNames.InfiniteTimeSpan);
}
