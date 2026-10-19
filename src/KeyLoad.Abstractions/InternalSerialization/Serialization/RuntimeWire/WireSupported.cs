using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Orleans.Serialization.Codecs;

namespace KeyLoad.Features.InternalSerialization;

// Only selected execution communication contracts, verified shapes and attributed owned DTOs enter dynamic decoding.
internal static class WireSupported
{
    private const int RootWireDepth = 1;
    private const int ChildDepthStep = 1;
    private const int VectorArrayRank = 1;
    private const string OwnedAssemblyPrefix = "KeyLoad.";

    internal static void Require(Type? type)
    {
        if (type is null)
        {
            return;
        }
        var normalizedRoot = WireSchema.Normalize(type)!;
        if (!normalizedRoot.ContainsGenericParameters && !normalizedRoot.IsGenericType
            && (normalizedRoot.IsPrimitive || normalizedRoot.IsEnum || IsScalar(normalizedRoot)))
        {
            WireCheck.Require(RootWireDepth <= SerializationLimits.WireDepth);
            return;
        }
        var pending = new Stack<(Type Type, int Depth)>();
        var visited = new HashSet<Type>();
        pending.Push((type, RootWireDepth));
        while (pending.TryPop(out var current))
        {
            var normalized = WireSchema.Normalize(current.Type)!;
            WireCheck.Require(current.Depth <= SerializationLimits.WireDepth && IsSupported(normalized));
            if (!visited.Add(normalized))
            {
                continue;
            }
            if (normalized.IsArray)
            {
                pending.Push((normalized.GetElementType()!, current.Depth + ChildDepthStep));
            }
            foreach (var argument in normalized.GenericTypeArguments)
            {
                pending.Push((argument, current.Depth + ChildDepthStep));
            }
        }
    }

    private static bool IsSupported(Type type)
    {
        if (type.ContainsGenericParameters)
        {
            return false;
        }
        if (type.IsPrimitive || type.IsEnum || IsScalar(type))
        {
            return true;
        }
        if (type.IsArray)
        {
            return type.GetArrayRank() == VectorArrayRank && type == type.GetElementType()!.MakeArrayType();
        }
        if (type.IsGenericType && IsCollection(type.GetGenericTypeDefinition()))
        {
            return true;
        }
        if (CommunicationContracts.Surrogate(type) is not null || CommunicationContracts.IsSurrogate(type))
        {
            return true;
        }
        return IsOwned(type);
    }

    private static bool IsScalar(Type type)
        => type == typeof(object) || type == typeof(string) || type == typeof(decimal)
            || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan)
            || type == typeof(Guid) || type == typeof(JsonElement) || type == typeof(WellKnownStringComparerCodec);

    private static bool IsCollection(Type definition)
        => definition == typeof(ImmutableArray<>) || definition == typeof(ImmutableDictionary<,>) || definition == typeof(List<>) || definition == typeof(Dictionary<,>)
            || definition == typeof(KeyValuePair<,>) || definition == typeof(Memory<>) || definition == typeof(ReadOnlyMemory<>);

    private static bool IsOwned(Type type)
    {
        if (type == typeof(ValueType))
        {
            return false;
        }
        for (var current = type; current is not null && current != typeof(object) && current != typeof(ValueType); current = current.BaseType)
        {
            if (current.Assembly.GetName().Name?.StartsWith(OwnedAssemblyPrefix, StringComparison.Ordinal) != true
                || current.GetCustomAttribute<Orleans.GenerateSerializerAttribute>(false) is null)
            {
                return false;
            }
        }
        return true;
    }
}
