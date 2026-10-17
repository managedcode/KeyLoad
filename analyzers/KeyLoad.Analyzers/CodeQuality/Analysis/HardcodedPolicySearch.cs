using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal sealed class HardcodedPolicySearch(Compilation compilation, CancellationToken cancellationToken, bool includeOtherConstants = false)
{
    private const int MaximumDepth = 32;
    private readonly HashSet<ISymbol> active = new(SymbolEqualityComparer.Default);

    internal bool IsHardcoded(IOperation operation) => Search(operation, MaximumDepth, new Dictionary<ISymbol, IOperation>(SymbolEqualityComparer.Default));

    private bool Search(IOperation operation, int depth, Dictionary<ISymbol, IOperation> arguments)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (depth <= 0 || HardcodedDurationPolicy.IsFixedNativeIdentity(compilation, operation))
        {
            return false;
        }
        if (operation.ConstantValue.HasValue && (includeOtherConstants || MagicRuntimeOperations.IsNumeric(operation.Type)))
        {
            return true;
        }
        return operation switch
        {
            IFieldReferenceOperation field when field.Field.IsReadOnly &&
                !HardcodedDurationPolicy.HasAuthoredWrites(compilation, field.Field, cancellationToken) =>
                SearchInitializer(field.Field, depth, arguments),
            ILocalReferenceOperation local when local.Local.RefKind == RefKind.None &&
                !HardcodedDurationPolicy.HasAuthoredWrites(compilation, local.Local, cancellationToken) =>
                SearchInitializer(local.Local, depth, arguments),
            IParameterReferenceOperation parameter => SearchParameter(parameter.Parameter, depth, arguments),
            IPropertyReferenceOperation property when !ConfigurationOwnership.IsOptionsType(compilation, property.Property.ContainingType) =>
                SearchReturns(property.Property, depth, arguments),
            IInvocationOperation invocation => SearchInvocation(invocation, depth, arguments),
            IObjectCreationOperation { Constructor: { } constructor } creation when HardcodedDurationPolicy.IsDurationMethod(compilation, constructor) =>
                creation.Arguments.Any(argument => NativeTimerPolicy.IsDurationArgument(compilation, constructor, argument) &&
                    Search(argument.Value, depth - 1, arguments)),
            IConditionalOperation conditional => conditional.WhenTrue is { } whenTrue && Search(whenTrue, depth - 1, arguments)
                || conditional.WhenFalse is { } whenFalse && Search(whenFalse, depth - 1, arguments),
            ICoalesceOperation coalesce => Search(coalesce.Value, depth - 1, arguments)
                || Search(coalesce.WhenNull, depth - 1, arguments),
            ISwitchExpressionOperation switchExpression => switchExpression.Arms.Any(arm => Search(arm.Value, depth - 1, arguments)),
            IConversionOperation conversion => Search(conversion.Operand, depth - 1, arguments),
            IUnaryOperation unary => Search(unary.Operand, depth - 1, arguments),
            IBinaryOperation binary => Search(binary.LeftOperand, depth - 1, arguments) || Search(binary.RightOperand, depth - 1, arguments),
            IParenthesizedOperation parenthesized => Search(parenthesized.Operand, depth - 1, arguments),
            _ => false
        };
    }

    private bool SearchInvocation(IInvocationOperation invocation, int depth, Dictionary<ISymbol, IOperation> arguments)
    {
        if (HardcodedDurationPolicy.IsDurationMethod(compilation, invocation.TargetMethod))
        {
            return invocation.Arguments.Any(argument => NativeTimerPolicy.IsDurationArgument(compilation, invocation.TargetMethod, argument) &&
                Search(argument.Value, depth - 1, arguments));
        }
        if (invocation.TargetMethod.DeclaringSyntaxReferences.IsEmpty)
        {
            return false;
        }
        var bound = new Dictionary<ISymbol, IOperation>(arguments, SymbolEqualityComparer.Default);
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter is { } parameter)
            {
                bound[parameter.OriginalDefinition] = argument.Value;
            }
        }
        return SearchReturns(invocation.TargetMethod, depth, bound);
    }

    private bool SearchInitializer(ISymbol symbol, int depth, Dictionary<ISymbol, IOperation> arguments)
    {
        if (!active.Add(symbol))
        {
            return false;
        }
        try
        {
            return symbol.DeclaringSyntaxReferences.Any(reference =>
                reference.GetSyntax(cancellationToken) is VariableDeclaratorSyntax { Initializer.Value: { } value } &&
                compilation.GetSemanticModel(value.SyntaxTree).GetOperation(value, cancellationToken) is { } initializer &&
                Search(initializer, depth - 1, arguments));
        }
        finally
        {
            active.Remove(symbol);
        }
    }

    private bool SearchReturns(ISymbol symbol, int depth, Dictionary<ISymbol, IOperation> arguments)
    {
        if (symbol.DeclaringSyntaxReferences.IsEmpty || !active.Add(symbol.OriginalDefinition))
        {
            return false;
        }
        try
        {
            return HardcodedPolicyReturns.Read(compilation, symbol, cancellationToken).Any(value => Search(value, depth - 1, arguments));
        }
        finally
        {
            active.Remove(symbol.OriginalDefinition);
        }
    }

    private bool SearchParameter(IParameterSymbol parameter, int depth, Dictionary<ISymbol, IOperation> arguments)
    {
        if (!active.Add(parameter.OriginalDefinition))
        {
            return false;
        }
        try
        {
            return arguments.TryGetValue(parameter.OriginalDefinition, out var supplied)
                ? Search(supplied, depth - 1, arguments)
                : PolicyArgumentSources.Read(compilation, parameter, cancellationToken).Any(value => Search(value, depth - 1, arguments));
        }
        finally
        {
            active.Remove(parameter.OriginalDefinition);
        }
    }
}
