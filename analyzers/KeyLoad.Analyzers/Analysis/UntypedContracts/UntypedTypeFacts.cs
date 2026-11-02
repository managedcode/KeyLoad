using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace KeyLoad.Analyzers.CodeQuality;

internal sealed class UntypedTypeFacts
{
    private readonly ConcurrentDictionary<ITypeSymbol, bool> cache = new(SymbolEqualityComparer.Default);
    private readonly HashSet<ITypeSymbol> collections = new(SymbolEqualityComparer.Default);

    internal UntypedTypeFacts(Compilation compilation)
    {
        foreach (var name in UntypedContractPolicy.UntypedCollections)
        {
            var type = compilation.GetTypeByMetadataName(name);
            if (type is not null && type.DeclaringSyntaxReferences.IsEmpty)
            {
                collections.Add(type);
            }
        }
    }

    internal bool Contains(ITypeSymbol? type, CancellationToken token)
    {
        if (type is null)
        {
            return false;
        }
        if (cache.TryGetValue(type, out var result))
        {
            return result;
        }
        result = Inspect(type, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default), token);
        return cache.GetOrAdd(type, result);
    }

    private bool Inspect(ITypeSymbol type, HashSet<ITypeSymbol> visited, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (type.SpecialType == SpecialType.System_Object || type.TypeKind == TypeKind.Dynamic
            || collections.Contains(type.OriginalDefinition))
        {
            return true;
        }
        if (!visited.Add(type))
        {
            return false;
        }
        if (type is IArrayTypeSymbol array)
        {
            return Inspect(array.ElementType, visited, token);
        }
        if (type is not INamedTypeSymbol named)
        {
            return false;
        }
        foreach (var argument in named.TypeArguments)
        {
            if (Inspect(argument, visited, token))
            {
                return true;
            }
        }
        return named.DelegateInvokeMethod is { } invoke && InspectSignature(invoke, visited, token);
    }

    private bool InspectSignature(IMethodSymbol method, HashSet<ITypeSymbol> visited, CancellationToken token)
    {
        if (Inspect(method.ReturnType, visited, token))
        {
            return true;
        }
        foreach (var parameter in method.Parameters)
        {
            if (Inspect(parameter.Type, visited, token))
            {
                return true;
            }
        }
        return false;
    }
}
