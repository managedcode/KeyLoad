using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;

namespace KeyLoad.Features.InternalSerialization;

internal static class ContractValidation
{
    private const int RootGraphDepth = 1;
    private const int LeafHeight = 0;
    private const int ChildDepthStep = 1;
    private const int DictionaryKeyIndex = 0;
    private const int DictionaryValueIndex = 1;
    private static readonly ConcurrentDictionary<Type, MemberValidation[]> Members = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo> CollectionDefaults = new();

    internal static void Validate<T>(T value, ValidationProfile profile = ValidationProfile.Strict)
    {
        if (value is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, PayloadVersion.InvalidPayload);
        }
        if (!IsScalar(value))
        {
            _ = Visit(value, new GraphValidationState(), null, profile, RootGraphDepth);
        }
    }

    private static int Visit(object? value, GraphValidationState state, ValueValidation? validation,
        ValidationProfile profile, int depth)
    {
        state.Charge();
        if (value is null)
        {
            if (validation?.Required is true)
            {
                throw Errors.Fail(ErrorCode.Corruption, PayloadVersion.InvalidPayload);
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

    private static int VisitChildren(object value, Type type, GraphValidationState state,
        ValueValidation? validation, ValidationProfile profile, int depth)
    {
        if (CommunicationContracts.TryProject(value, out var surrogate))
        {
            return Visit(surrogate, state, null, profile, depth);
        }
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
                if (item is not null || profile != ValidationProfile.PublicInputElements)
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

    private static int VisitMembers(object value, Type type, GraphValidationState state,
        ValueValidation? validation, ValidationProfile profile, int depth)
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
            throw Errors.Fail(ErrorCode.Corruption, PayloadVersion.InvalidPayload);
        }
    }

    private static MemberValidation[] GetMembers(Type type)
    {
        var context = new NullabilityInfoContext();
        var members = new List<MemberValidation>();
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
                    members.Add(new(property.GetValue, ValueValidation.Create(context.Create(property))));
                }
            }
            foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.IsDefined(typeof(Orleans.IdAttribute)))
                {
                    members.Add(new(field.GetValue, ValueValidation.Create(context.Create(field))));
                }
            }
        }
        return members.ToArray();
    }
}
