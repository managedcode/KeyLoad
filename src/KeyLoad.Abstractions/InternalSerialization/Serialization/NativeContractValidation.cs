using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;

namespace KeyLoad.Features.InternalSerialization;

internal static class NativeContractValidation
{
    private const int RootGraphDepth = 1;
    private const int LeafHeight = 0;
    private const int ChildDepthStep = 1;
    private const int DictionaryKeyIndex = 0;
    private const int DictionaryValueIndex = 1;
    private static readonly ConcurrentDictionary<Type, NativeMemberValidation[]> Members = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo> CollectionDefaults = new();

    internal static void Validate<T>(T value, NativeValidationProfile profile = NativeValidationProfile.Strict)
    {
        if (value is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, NativePayloadVersion.InvalidPayload);
        }
        if (!IsScalar(value))
        {
            _ = Visit(value, new NativeGraphValidationState(), null, profile, RootGraphDepth);
        }
    }

    private static int Visit(object? value, NativeGraphValidationState state, NativeValueValidation? validation,
        NativeValidationProfile profile, int depth)
    {
        state.Charge();
        if (value is null)
        {
            if (validation?.Required is true)
            {
                throw Errors.Fail(ErrorCode.Corruption, NativePayloadVersion.InvalidPayload);
            }
            return LeafHeight;
        }
        var type = value.GetType();
        if (IsScalar(value))
        {
            return LeafHeight;
        }
        var tracked = !type.IsValueType;
        if (state.Enter(value, tracked, validation, depth, out var completedHeight))
        {
            return completedHeight;
        }
        try
        {
            var height = checked(ChildDepthStep + VisitChildren(value, type, state, validation, profile, depth + ChildDepthStep));
            state.Complete(value, tracked, validation, height);
            return height;
        }
        finally
        {
            state.Leave(value, tracked);
        }
    }

    private static int VisitChildren(object value, Type type, NativeGraphValidationState state,
        NativeValueValidation? validation, NativeValidationProfile profile, int depth)
    {
        ValidateCollection(value, type);
        var height = LeafHeight;
        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                height = Math.Max(height, Visit(entry.Key, state, validation?.DictionaryKey, profile, depth));
                height = Math.Max(height, Visit(entry.Value, state, validation?.DictionaryValue, profile, depth));
            }
            return height;
        }
        if (value is IEnumerable sequence)
        {
            foreach (var item in sequence)
            {
                // Public JSON permits null reference elements; their owning validators preserve error precedence.
                if (item is not null || profile != NativeValidationProfile.PublicInputElements)
                {
                    height = Math.Max(height, Visit(item, state, validation?.Element ?? validation, profile, depth));
                }
                else
                {
                    state.Charge();
                }
            }
            return height;
        }
        return VisitMembers(value, type, state, validation, profile, depth);
    }

    private static bool IsScalar(object value)
        => value.GetType().IsPrimitive || value.GetType().IsEnum
            || value is string or decimal or DateTime or DateTimeOffset or TimeSpan or Guid or JsonElement
                or ReadOnlyMemory<byte> or Memory<byte> or byte[];

    private static int VisitMembers(object value, Type type, NativeGraphValidationState state,
        NativeValueValidation? validation, NativeValidationProfile profile, int depth)
    {
        var members = Members.GetOrAdd(type, GetMembers);
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
        {
            return Math.Max(Visit(members[DictionaryKeyIndex].Read(value), state, validation?.DictionaryKey, profile, depth),
                Visit(members[DictionaryValueIndex].Read(value), state, validation?.DictionaryValue, profile, depth));
        }
        var height = LeafHeight;
        foreach (var member in members)
        {
            height = Math.Max(height, Visit(member.Read(value), state, member.Value, profile, depth));
        }
        return height;
    }

    private static void ValidateCollection(object value, Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ImmutableArray<>)
            && CollectionDefaults.GetOrAdd(type, static current => current.GetProperty(nameof(ImmutableArray<int>.IsDefault))!).GetValue(value) is true)
        {
            throw Errors.Fail(ErrorCode.Corruption, NativePayloadVersion.InvalidPayload);
        }
    }

    private static NativeMemberValidation[] GetMembers(Type type)
    {
        var context = new NullabilityInfoContext();
        var members = new List<NativeMemberValidation>();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
        {
            return [new(type.GetProperty(nameof(KeyValuePair<int, int>.Key))!.GetValue, null),
                new(type.GetProperty(nameof(KeyValuePair<int, int>.Value))!.GetValue, null)];
        }
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var property in current.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (property.IsDefined(typeof(Orleans.IdAttribute)))
                {
                    members.Add(new(property.GetValue, NativeValueValidation.Create(context.Create(property))));
                }
            }
            foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.IsDefined(typeof(Orleans.IdAttribute)))
                {
                    members.Add(new(field.GetValue, NativeValueValidation.Create(context.Create(field))));
                }
            }
        }
        return members.ToArray();
    }
}

internal sealed record NativeMemberValidation(Func<object, object?> Read, NativeValueValidation? Value);

internal sealed record NativeValueValidation(bool Required, NativeValueValidation? Element,
    NativeValueValidation? DictionaryKey = null, NativeValueValidation? DictionaryValue = null)
{
    private const int DictionaryArgumentCount = 2;
    private const int SequenceArgumentCount = 1;
    private const int FirstTypeArgumentIndex = 0;
    private const int ValueTypeArgumentIndex = 1;
    internal static NativeValueValidation Create(NullabilityInfo info)
    {
        var required = !info.Type.IsValueType && info.ReadState == NullabilityState.NotNull;
        if (info.ElementType is { } element)
        {
            return new(required, Create(element));
        }
        // NullabilityInfo keeps Nullable<T> as Type, but its generic metadata already describes T.
        var arguments = info.GenericTypeArguments;
        var type = Nullable.GetUnderlyingType(info.Type) ?? info.Type;
        if (arguments.Length == DictionaryArgumentCount && (IsKeyValuePair(type) || IsDictionary(type) || type.GetInterfaces().Any(IsDictionary)))
        {
            return new(required, null, Create(arguments[FirstTypeArgumentIndex]), Create(arguments[ValueTypeArgumentIndex]));
        }
        return new(required, arguments.Length == SequenceArgumentCount ? Create(arguments[FirstTypeArgumentIndex]) : null);
    }

    private static bool IsDictionary(Type type)
        => type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(IDictionary<,>)
            || type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>));

    private static bool IsKeyValuePair(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>);
}
