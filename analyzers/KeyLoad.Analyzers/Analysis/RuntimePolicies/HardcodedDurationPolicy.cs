using System;
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
        if (NativeTimerPolicy.IsDurationMethod(compilation, method))
        {
            return true;
        }
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
        new HardcodedPolicySearch(compilation, cancellationToken).IsHardcoded(operation);

    internal static bool IsHardcodedOptionsOverride(Compilation compilation, IOperation operation, CancellationToken cancellationToken) =>
        new HardcodedPolicySearch(compilation, cancellationToken, includeOtherConstants: true).IsHardcoded(operation);

    internal static bool HasAuthoredWrites(Compilation compilation, ISymbol symbol, CancellationToken cancellationToken)
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

    internal static bool IsFixedNativeIdentity(Compilation compilation, IOperation operation) =>
        operation is IFieldReferenceOperation field &&
        (MagicRuntimeOperations.IsNativeType(compilation, field.Field.ContainingType, MagicRuntimeMetadataNames.TimeSpan) &&
            field.Field.Name is ConfigurationMetadataNames.Zero or ConfigurationMetadataNames.MinValue or ConfigurationMetadataNames.MaxValue ||
         MagicRuntimeOperations.IsNativeType(compilation, field.Field.ContainingType, ConfigurationMetadataNames.Timeout) &&
            field.Field.Name is ConfigurationMetadataNames.Infinite or ConfigurationMetadataNames.InfiniteTimeSpan);
}
