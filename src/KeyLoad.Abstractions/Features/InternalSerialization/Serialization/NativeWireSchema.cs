using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Orleans.Serialization.Codecs;

namespace KeyLoad.Features.InternalSerialization;

// Resolve omitted native headers from attributed member contracts, including native surrogates.
internal static class NativeWireSchema
{
    private const int RootScope = 0;
    private const int FirstMemberId = 0;
    private const int ElementTypeArgumentIndex = 0;
    private const int KeyValueTypeArgumentCount = 2;
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<IReadOnlyDictionary<uint, Type>>> Members = new();

    internal static Type? Normalize(Type? type) => type is null ? null : Nullable.GetUnderlyingType(type) ?? type;

    internal static bool Compatible(Type expected, Type actual)
    {
        expected = Normalize(expected)!;
        if (actual == typeof(WellKnownStringComparerCodec))
        {
            return expected == typeof(object) || expected == typeof(IEqualityComparer<string>) || expected == typeof(StringComparer);
        }
        return expected.IsAssignableFrom(actual) || expected.IsEnum && Enum.GetUnderlyingType(expected) == actual;
    }

    internal static Type? ReferenceType(Type? type)
    {
        if (type?.IsGenericType == true)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(ReadOnlyMemory<>) || definition == typeof(Memory<>))
            {
                return type.GenericTypeArguments[ElementTypeArgumentIndex].MakeArrayType();
            }
        }
        return type;
    }

    internal static Type? Member(Type? type, int scope, uint id)
    {
        if (type is null)
        {
            return null;
        }
        type = Normalize(type)!;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ImmutableArray<>))
        {
            return scope == RootScope && id == FirstMemberId ? type.GenericTypeArguments[ElementTypeArgumentIndex].MakeArrayType() : null;
        }
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
        {
            return scope == RootScope && id < KeyValueTypeArgumentCount ? type.GenericTypeArguments[id] : null;
        }
        var scopes = Members.GetOrAdd(type == typeof(JsonElement) ? typeof(JsonElementSurrogate) : type, Create);
        return scope < scopes.Count && scopes[scope].TryGetValue(id, out var member) ? member : null;
    }

    private static IReadOnlyList<IReadOnlyDictionary<uint, Type>> Create(Type type)
    {
        var hierarchy = new Stack<Type>();
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            if (current.GetCustomAttribute<Orleans.GenerateSerializerAttribute>(false) is not null)
            {
                hierarchy.Push(current);
            }
        }
        var result = new List<IReadOnlyDictionary<uint, Type>>();
        foreach (var current in hierarchy)
        {
            result.AddRange(NativeWireMemberScopes.Create(current));
        }
        return result;
    }
}
