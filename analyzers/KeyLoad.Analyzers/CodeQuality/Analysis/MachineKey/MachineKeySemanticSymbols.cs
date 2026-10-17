using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class MachineKeySemanticSymbols
{
    public static IMethodSymbol? ResolveMethod(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation)
    {
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        return symbolInfo.Symbol as IMethodSymbol ??
               symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
    }

    public static bool IsKeyArgument(
        SyntaxNodeAnalysisContext context,
        IMethodSymbol? method,
        ArgumentSyntax argument,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        bool allowFirstLogicalParameter,
        bool allowUnboundFallback)
    {
        var parameter = (context.SemanticModel.GetOperation(argument, context.CancellationToken) as IArgumentOperation)
            ?.Parameter ?? ResolveParameter(method, argument, arguments);
        if (parameter is not null)
        {
            var parameterMethod = parameter.ContainingSymbol as IMethodSymbol ?? method;
            if (IsUnreducedExtensionReceiver(parameterMethod, parameter))
            {
                return false;
            }

            return IsExplicitKeyParameter(parameter.Name) ||
                   allowFirstLogicalParameter && IsFirstLogicalParameter(parameterMethod, parameter);
        }

        var namedArgument = argument.NameColon?.Name.Identifier.ValueText;
        if (namedArgument is not null)
        {
            return allowUnboundFallback && IsExplicitKeyParameter(namedArgument);
        }

        return allowUnboundFallback &&
               allowFirstLogicalParameter &&
               arguments.IndexOf(argument) == 0 &&
               argument.NameColon is null;
    }

    private static IParameterSymbol? ResolveParameter(
        IMethodSymbol? method,
        ArgumentSyntax argument,
        SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        if (method is null)
        {
            return null;
        }

        var namedArgument = argument.NameColon?.Name.Identifier.ValueText;
        if (namedArgument is not null)
        {
            return method.Parameters.FirstOrDefault(parameter =>
                string.Equals(parameter.Name, namedArgument, StringComparison.Ordinal));
        }

        var nextParameter = 0;
        foreach (var candidate in arguments)
        {
            if (candidate.NameColon is not null)
            {
                continue;
            }

            while (nextParameter < method.Parameters.Length &&
                   HasNamedArgument(method.Parameters[nextParameter], arguments))
            {
                nextParameter++;
            }

            if (nextParameter >= method.Parameters.Length)
            {
                return method.Parameters.LastOrDefault(parameter => parameter.IsParams);
            }

            var parameter = method.Parameters[nextParameter];
            if (parameter.IsParams || candidate == argument)
            {
                return parameter;
            }

            nextParameter++;
        }

        return null;
    }

    private static bool HasNamedArgument(
        IParameterSymbol parameter,
        SeparatedSyntaxList<ArgumentSyntax> arguments) =>
        arguments.Any(argument => string.Equals(
            argument.NameColon?.Name.Identifier.ValueText,
            parameter.Name,
            StringComparison.Ordinal));

    private static bool IsUnreducedExtensionReceiver(IMethodSymbol? method, IParameterSymbol parameter) =>
        method is { IsExtensionMethod: true, ReducedFrom: null } && parameter.Ordinal == 0;

    private static bool IsFirstLogicalParameter(IMethodSymbol? method, IParameterSymbol parameter)
    {
        if (method is null)
        {
            return false;
        }

        var firstLogicalOrdinal = method.IsExtensionMethod && method.ReducedFrom is null ? 1 : 0;
        return parameter.Ordinal == firstLogicalOrdinal;
    }

    public static bool IsKeyedContainer(IMethodSymbol? method)
    {
        if (method?.ContainingType is not { } type)
        {
            return false;
        }

        if (IsKnownKeyValueType(type) ||
            type.Name.Contains(MachineKeyTypeNames.Dictionary, StringComparison.Ordinal) ||
            type.Name.Contains(MachineKeyTypeNames.Headers, StringComparison.Ordinal) ||
            type.Name is MachineKeyTypeNames.JObject or
                MachineKeyTypeNames.JsonObject or
                MachineKeyTypeNames.NameValueCollection or
                MachineKeyTypeNames.RouteValueDictionary or
                MachineKeyTypeNames.TagList)
        {
            return true;
        }

        return type.AllInterfaces.Any(interfaceType =>
            interfaceType.Name is MachineKeyTypeNames.IDictionary or MachineKeyTypeNames.IReadOnlyDictionary);
    }

    public static bool IsKnownKeyValueType(INamedTypeSymbol type) =>
        type.Name is MachineKeyTypeNames.KeyValuePair or
            MachineKeyTypeNames.DictionaryEntry or
            MachineKeyTypeNames.EndpointQueryParameter or
            MachineKeyTypeNames.TagList ||
        type.Name.EndsWith(MachineKeyTypeNames.QueryParameterSuffix, StringComparison.Ordinal);

    public static bool IsExplicitKeyParameter(string parameterName) =>
        MachineKeyRuleCatalog.ExplicitKeyParameterNames.Contains(parameterName) ||
        parameterName.EndsWith(MachineKeyParameterNames.Key, StringComparison.OrdinalIgnoreCase);

    public static string InvocationName(ExpressionSyntax expression) =>
        expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => string.Empty,
        };

    public static string AttributeName(NameSyntax name) =>
        name switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
            _ => name.ToString().Split('.').Last(),
        };
}
