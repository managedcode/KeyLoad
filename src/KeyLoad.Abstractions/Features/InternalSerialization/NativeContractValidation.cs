using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;

namespace KeyLoad.Features.InternalSerialization;

internal static class NativeContractValidation
{
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
            _ = Visit(value, new NativeGraphValidationState(), null, profile, 1);
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
            return 0;
        }
        var type = value.GetType();
        if (IsScalar(value))
        {
            return 0;
        }
        var tracked = !type.IsValueType;
        if (state.Enter(value, tracked, validation, depth, out var completedHeight))
        {
            return completedHeight;
        }
        try
        {
            var height = checked(1 + VisitChildren(value, type, state, validation, profile, depth + 1));
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
        var height = 0;
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
            return Math.Max(Visit(members[0].Read(value), state, validation?.DictionaryKey, profile, depth),
                Visit(members[1].Read(value), state, validation?.DictionaryValue, profile, depth));
        }
        var height = 0;
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
    internal static NativeValueValidation Create(NullabilityInfo info)
    {
        var required = !info.Type.IsValueType && info.ReadState == NullabilityState.NotNull;
        if (Nullable.GetUnderlyingType(info.Type) is not null)
        {
            return Create(info.GenericTypeArguments[0]);
        }
        if (info.ElementType is { } element)
        {
            return new(required, Create(element));
        }
        var arguments = info.GenericTypeArguments;
        if (arguments.Length == 2 && (IsDictionary(info.Type) || info.Type.GetInterfaces().Any(IsDictionary)))
        {
            return new(required, null, Create(arguments[0]), Create(arguments[1]));
        }
        return new(required, arguments.Length == 1 ? Create(arguments[0]) : null);
    }

    private static bool IsDictionary(Type type)
        => type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(IDictionary<,>)
            || type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>));
}
