using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Orleans.Serialization.Codecs;

namespace KeyLoad.Features.InternalSerialization;

// Only verified native shapes and attributed owned DTOs may enter dynamic decoding.
// Other Orleans codecs/surrogates require their own schema and qualification before admission.
internal static class NativeWireSupported
{
    private const string OwnedAssemblyPrefix = "KeyLoad.";

    internal static void Require(Type? type)
    {
        if (type is null)
        {
            return;
        }
        var normalizedRoot = NativeWireSchema.Normalize(type)!;
        if (!normalizedRoot.ContainsGenericParameters && !normalizedRoot.IsGenericType
            && (normalizedRoot.IsPrimitive || normalizedRoot.IsEnum || IsScalar(normalizedRoot)))
        {
            NativeWireCheck.Require(1 <= NativeSerializationLimits.WireDepth);
            return;
        }
        var pending = new Stack<(Type Type, int Depth)>();
        var visited = new HashSet<Type>();
        pending.Push((type, 1));
        while (pending.TryPop(out var current))
        {
            var normalized = NativeWireSchema.Normalize(current.Type)!;
            NativeWireCheck.Require(current.Depth <= NativeSerializationLimits.WireDepth && IsSupported(normalized));
            if (!visited.Add(normalized))
            {
                continue;
            }
            if (normalized.IsArray)
            {
                pending.Push((normalized.GetElementType()!, current.Depth + 1));
            }
            foreach (var argument in normalized.GenericTypeArguments)
            {
                pending.Push((argument, current.Depth + 1));
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
            return type.GetArrayRank() == 1 && type == type.GetElementType()!.MakeArrayType();
        }
        if (type.IsGenericType && IsCollection(type.GetGenericTypeDefinition()))
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
        => definition == typeof(ImmutableArray<>) || definition == typeof(List<>) || definition == typeof(Dictionary<,>)
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
