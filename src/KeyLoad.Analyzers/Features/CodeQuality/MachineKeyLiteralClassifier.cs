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
        return MachineKeyRuleCatalog.AlwaysKeyMethodNames.Contains(methodName);
    }

    private static bool IsMachineKeyInvocationArgument(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        ArgumentSyntax argument)
    {
        var symbol = MachineKeySemanticSymbols.ResolveMethod(context, invocation);
        var parameter = MachineKeySemanticSymbols.ResolveParameter(
            symbol,
            argument,
            invocation.ArgumentList.Arguments);
        if (parameter is not null && MachineKeySemanticSymbols.IsExplicitKeyParameter(parameter.Name))
        {
            return true;
        }

        if (invocation.ArgumentList.Arguments.IndexOf(argument) != 0)
        {
            return false;
        }

        var methodName = symbol?.Name ?? MachineKeySemanticSymbols.InvocationName(invocation.Expression);
        if (MachineKeyRuleCatalog.AlwaysKeyMethodNames.Contains(methodName))
        {
            return true;
        }

        return methodName is MachineKeyMethodNames.Add or
                   MachineKeyMethodNames.Remove or
                   MachineKeyMethodNames.TryAdd or
                   MachineKeyMethodNames.Append or
                   MachineKeyMethodNames.Set &&
               MachineKeySemanticSymbols.IsKeyedContainer(symbol);
    }

    private static bool IsMachineKeyCreationArgument(
        SyntaxNodeAnalysisContext context,
        BaseObjectCreationExpressionSyntax creation,
        ArgumentSyntax argument)
    {
        if (creation.ArgumentList is not { } argumentList)
        {
            return false;
        }

        var constructor = context.SemanticModel.GetSymbolInfo(creation, context.CancellationToken)
            .Symbol as Microsoft.CodeAnalysis.IMethodSymbol;
        var parameter = MachineKeySemanticSymbols.ResolveParameter(
            constructor,
            argument,
            argumentList.Arguments);
        if (parameter is not null && MachineKeySemanticSymbols.IsExplicitKeyParameter(parameter.Name))
        {
            return true;
        }

        var containingType = constructor?.ContainingType;
        return argumentList.Arguments.IndexOf(argument) == 0 &&
               containingType is not null &&
               MachineKeySemanticSymbols.IsKnownKeyValueType(containingType);
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
