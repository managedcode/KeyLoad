using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

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

    public static IParameterSymbol? ResolveParameter(
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

        var index = arguments.IndexOf(argument);
        return index >= 0 && index < method.Parameters.Length ? method.Parameters[index] : null;
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
