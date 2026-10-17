using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class ConfiguredCountingSignal
{
    internal static bool IsConfigured(Compilation compilation, IObjectCreationOperation creation, CancellationToken cancellationToken) =>
        MagicRuntimeOperations.IsNativeType(compilation, creation.Type, MagicRuntimeMetadataNames.Semaphore) &&
        creation.Arguments is [var initial, var maximum] &&
        initial.Value.ConstantValue is { HasValue: true, Value: int count } && count == OperationalPolicyMetadataNames.EmptySignalCount &&
        IsCapacity(compilation, maximum.Value, new HashSet<ISymbol>(SymbolEqualityComparer.Default), cancellationToken);

    private static bool IsCapacity(Compilation compilation, IOperation operation, HashSet<ISymbol> visited, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return operation switch
        {
            IConversionOperation conversion => IsCapacity(compilation, conversion.Operand, visited, cancellationToken),
            IParenthesizedOperation parenthesized => IsCapacity(compilation, parenthesized.Operand, visited, cancellationToken),
            IPropertyReferenceOperation property when ConfigurationOwnership.IsOptionsType(compilation, property.Property.ContainingType) =>
                property.Instance is { } instance && IsSnapshot(compilation, instance, cancellationToken),
            IBinaryOperation { OperatorKind: BinaryOperatorKind.Add } binary =>
                WakeUnits(binary) <= OperationalPolicyMetadataNames.StopWakeCount &&
                (IsCapacity(compilation, binary.LeftOperand, visited, cancellationToken) && IsWakeUnit(binary.RightOperand) ||
                 IsWakeUnit(binary.LeftOperand) && IsCapacity(compilation, binary.RightOperand, visited, cancellationToken) ||
                 IsCapacity(compilation, binary.LeftOperand, visited, cancellationToken) &&
                 IsCapacity(compilation, binary.RightOperand, visited, cancellationToken)),
            IInvocationOperation invocation => IsSaturated(compilation, invocation, visited, cancellationToken) ||
                IsPrivateHelper(compilation, invocation.TargetMethod, visited, cancellationToken),
            _ => false
        };
    }

    private static bool IsSaturated(Compilation compilation, IInvocationOperation invocation, HashSet<ISymbol> visited,
        CancellationToken cancellationToken) =>
        invocation.TargetMethod.Name == OperationalPolicyMetadataNames.Minimum &&
        MagicRuntimeOperations.IsNativeType(compilation, invocation.TargetMethod.ContainingType, OperationalPolicyMetadataNames.Math) &&
        invocation.Arguments is [var ceiling, var configured] && IsNativeIntegerCeiling(ceiling.Value) &&
        IsCapacity(compilation, configured.Value, visited, cancellationToken);

    private static bool IsNativeIntegerCeiling(IOperation operation) => operation switch
    {
        IConversionOperation conversion => IsNativeIntegerCeiling(conversion.Operand),
        IFieldReferenceOperation field => field.Field.ContainingType.SpecialType == SpecialType.System_Int32 &&
            field.Field.Name == nameof(int.MaxValue),
        _ => false
    };

    private static bool IsWakeUnit(IOperation operation) =>
        operation.ConstantValue is { HasValue: true, Value: int count } && count == OperationalPolicyMetadataNames.StopWakeCount ||
        operation.ConstantValue is { HasValue: true, Value: long wideCount } && wideCount == OperationalPolicyMetadataNames.StopWakeCount;

    private static int WakeUnits(IOperation operation) => operation switch
    {
        IConversionOperation conversion => WakeUnits(conversion.Operand),
        IBinaryOperation { OperatorKind: BinaryOperatorKind.Add } binary => WakeUnits(binary.LeftOperand) + WakeUnits(binary.RightOperand),
        _ => IsWakeUnit(operation) ? OperationalPolicyMetadataNames.StopWakeCount : OperationalPolicyMetadataNames.EmptySignalCount
    };

    private static bool IsPrivateHelper(Compilation compilation, IMethodSymbol method, HashSet<ISymbol> visited,
        CancellationToken cancellationToken)
    {
        if (!method.IsStatic || method.DeclaredAccessibility != Accessibility.Private || !visited.Add(method))
        {
            return false;
        }

        try
        {
            return method.DeclaringSyntaxReferences.Any(reference =>
                reference.GetSyntax(cancellationToken) is MethodDeclarationSyntax declaration &&
                FindReturn(declaration) is { } expression &&
                compilation.GetSemanticModel(expression.SyntaxTree).GetOperation(expression, cancellationToken) is { } result &&
                IsCapacity(compilation, result, visited, cancellationToken));
        }
        finally
        {
            visited.Remove(method);
        }
    }

    private static ExpressionSyntax? FindReturn(MethodDeclarationSyntax method) =>
        method.ExpressionBody?.Expression ??
        (method.Body?.Statements.OfType<ReturnStatementSyntax>().Take(2).ToArray() is [var statement] ? statement.Expression : null);

    private static bool IsSnapshot(Compilation compilation, IOperation operation, CancellationToken cancellationToken) => operation switch
    {
        IPropertyReferenceOperation property =>
            property.Property.Name == ConfigurationMetadataNames.Value &&
                ConfigurationOwnership.IsOptionsWrapper(compilation, property.Property.ContainingType) ||
            ConfigurationOwnership.IsOptionsType(compilation, property.Type) &&
                OptionsSnapshotCapture.IsCaptured(compilation, property.Property, cancellationToken),
        IFieldReferenceOperation field => ConfigurationOwnership.IsOptionsType(compilation, field.Type) &&
            OptionsSnapshotCapture.IsCaptured(compilation, field.Field, cancellationToken),
        ILocalReferenceOperation local => IsLocalSnapshot(compilation, local, cancellationToken),
        IConversionOperation conversion => IsSnapshot(compilation, conversion.Operand, cancellationToken),
        _ => false
    };

    private static bool IsLocalSnapshot(Compilation compilation, ILocalReferenceOperation local, CancellationToken cancellationToken)
    {
        if (local.Local.DeclaringSyntaxReferences.SingleOrDefault()?.GetSyntax(cancellationToken) is not
            VariableDeclaratorSyntax { Initializer.Value: { } initializer } variable ||
            initializer.Span.End >= local.Syntax.SpanStart ||
            variable.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault() is not { } owner)
        {
            return false;
        }

        var model = compilation.GetSemanticModel(initializer.SyntaxTree);
        return !owner.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any(assignment =>
            SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(assignment.Left, cancellationToken).Symbol, local.Local)) &&
            !owner.DescendantNodes().OfType<ArgumentSyntax>().Any(argument =>
                !argument.RefKindKeyword.IsKind(SyntaxKind.None) &&
                SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(argument.Expression, cancellationToken).Symbol, local.Local)) &&
            model.GetOperation(initializer, cancellationToken) is { } value && IsSnapshot(compilation, value, cancellationToken);
    }
}
