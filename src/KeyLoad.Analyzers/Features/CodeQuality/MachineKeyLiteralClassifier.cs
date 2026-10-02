using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class MachineKeyLiteralClassifier
{
    public static bool IsMachineKey(
        SyntaxNodeAnalysisContext context,
        LiteralExpressionSyntax literal)
    {
        if (IsNestedAlwaysKeyInvocationArgument(context, literal))
        {
            return true;
        }

        if (literal.Parent is ArgumentSyntax argument &&
            IsMachineKeyArgument(context, argument))
        {
            return true;
        }

        if (literal.Parent is InitializerExpressionSyntax initializer &&
            initializer.IsKind(SyntaxKind.ComplexElementInitializerExpression) &&
            initializer.Expressions.FirstOrDefault() == literal)
        {
            return true;
        }

        return literal.Parent is AttributeArgumentSyntax attributeArgument &&
               IsMachineKeyAttributeArgument(attributeArgument);
    }

    private static bool IsMachineKeyArgument(
        SyntaxNodeAnalysisContext context,
        ArgumentSyntax argument)
    {
        if (argument.Parent is BracketedArgumentListSyntax)
        {
            return true;
        }

        if (argument.Parent is not ArgumentListSyntax argumentList)
        {
            return false;
        }

        if (argumentList.Parent is InvocationExpressionSyntax invocation)
        {
            return IsMachineKeyInvocationArgument(context, invocation, argument);
        }

        return argumentList.Parent is BaseObjectCreationExpressionSyntax creation &&
               IsMachineKeyCreationArgument(context, creation, argument);
    }

    private static bool IsNestedAlwaysKeyInvocationArgument(
        SyntaxNodeAnalysisContext context,
        LiteralExpressionSyntax literal)
    {
        var argument = literal.Ancestors().OfType<ArgumentSyntax>().FirstOrDefault();
        if (argument?.Parent is not ArgumentListSyntax { Parent: InvocationExpressionSyntax invocation })
        {
            return false;
        }

        var methodName = MachineKeySemanticSymbols.ResolveMethod(context, invocation)?.Name ??
                         MachineKeySemanticSymbols.InvocationName(invocation.Expression);
        return MachineKeyRuleCatalog.AlwaysKeyMethodNames.Contains(methodName) &&
               IsMachineKeyInvocationArgument(context, invocation, argument);
    }

    private static bool IsMachineKeyInvocationArgument(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        ArgumentSyntax argument)
    {
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        var boundMethod = symbolInfo.Symbol as IMethodSymbol;
        var method = boundMethod ?? MachineKeySemanticSymbols.ResolveMethod(context, invocation);
        var methodName = method?.Name ?? MachineKeySemanticSymbols.InvocationName(invocation.Expression);
        var allowFirstLogicalParameter = MachineKeyRuleCatalog.AlwaysKeyMethodNames.Contains(methodName) ||
                                         IsKeyedMutationMethod(methodName) &&
                                         MachineKeySemanticSymbols.IsKeyedContainer(method);
        return MachineKeySemanticSymbols.IsKeyArgument(
            context,
            boundMethod,
            argument,
            invocation.ArgumentList.Arguments,
            allowFirstLogicalParameter,
            allowUnboundFallback: boundMethod is null);
    }

    private static bool IsKeyedMutationMethod(string methodName) =>
        methodName is MachineKeyMethodNames.Add or
            MachineKeyMethodNames.Remove or
            MachineKeyMethodNames.TryAdd or
            MachineKeyMethodNames.Append or
            MachineKeyMethodNames.Set;

    private static bool IsMachineKeyCreationArgument(
        SyntaxNodeAnalysisContext context,
        BaseObjectCreationExpressionSyntax creation,
        ArgumentSyntax argument)
    {
        if (creation.ArgumentList is not { } argumentList)
        {
            return false;
        }

        if (context.SemanticModel.GetSymbolInfo(creation, context.CancellationToken)
            .Symbol is not IMethodSymbol constructor)
        {
            return false;
        }

        var containingType = constructor.ContainingType;
        var allowFirstLogicalParameter = containingType is not null &&
                                         MachineKeySemanticSymbols.IsKnownKeyValueType(containingType);
        return MachineKeySemanticSymbols.IsKeyArgument(
            context,
            constructor,
            argument,
            argumentList.Arguments,
            allowFirstLogicalParameter,
            allowUnboundFallback: false);
    }

    private static bool IsMachineKeyAttributeArgument(AttributeArgumentSyntax argument)
    {
        if (argument.Parent?.Parent is not AttributeSyntax attribute)
        {
            return false;
        }

        var attributeName = MachineKeySemanticSymbols.AttributeName(attribute.Name);
        if (MachineKeyRuleCatalog.AllStringArgumentAttributes.Contains(attributeName))
        {
            return true;
        }

        if (!MachineKeyRuleCatalog.NamedStringArgumentAttributes.TryGetValue(attributeName, out var names))
        {
            return false;
        }

        var argumentName = argument.NameEquals?.Name.Identifier.ValueText ??
                           argument.NameColon?.Name.Identifier.ValueText;
        return argumentName is not null && names.Contains(argumentName) ||
               argumentName is null &&
               attributeName.StartsWith(MachineKeyAttributeNames.JsonProperty, StringComparison.Ordinal);
    }
}
