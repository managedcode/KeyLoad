using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;

namespace KeyLoad.InternalSerialization;

internal static class ContractValidation
{
    private static readonly ConcurrentDictionary<Type, MemberValidation[]> Members = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo> CollectionDefaults = new();

    internal static void Validate<T>(T value, ValidationProfile profile = ValidationProfile.Strict)
    {
        // Nonnullable value scalars are decided from T before a generic null check can box them.
        if (typeof(T).IsValueType && IsScalar(typeof(T)))
        {
            return;
        }
        if (value is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, PayloadVersion.InvalidPayload);
        }
        if (!IsScalar(typeof(T)) && !IsScalar(value.GetType()))
        {
            _ = (Visit(value /* KLD0042: Non-scalar value contracts enter reflection validation here; primitive value scalars return before this conversion. */, new GraphValidationState(), null, profile, 1) /* KLD0041: This boundary starts the bounded heterogeneous Id-member graph after the generic scalar checks. */);
        }
    }

    private static int Visit(object? /* KLD0041: Reflected Id members and runtime collection elements have heterogeneous declared types and nullable values. */ value, GraphValidationState state, ValueValidation? validation,
        ValidationProfile profile, int depth)
    {
        state.Charge();
        if (value is null)
        {
            if (validation?.Required is true)
            {
                throw Errors.Fail(ErrorCode.Corruption, PayloadVersion.InvalidPayload);
            }
            return 0;
        }
        var type = value.GetType() /* KLD0041: Runtime Id-member metadata is selected from the current nonnull graph value. */;
        if (IsScalar(type))
        {
            return 0;
        }
        var tracked = !type.IsValueType;
        if (state.Enter(value, tracked, validation, depth, out var completedHeight) /* KLD0041: The graph state tracks only reference values by identity and preserves this path nullability contract. */)
        {
            return completedHeight;
        }
        try
        {
            var height = checked(1 + (VisitChildren(value, type, state, validation, profile, depth + 1) /* KLD0041: Child traversal follows the current runtime type and applicable Id-member nullability metadata. */));
            state.Complete(value, tracked, validation, height) /* KLD0041: Completed reference identity and semantic height are retained for this validated graph path. */;
            return height;
        }
        finally
        {
            state.Leave(value, tracked) /* KLD0041: The same current reference identity is removed when its active traversal finishes. */;
        }
    }

    private static int VisitChildren(object /* KLD0041: Child enumeration is selected from the concrete runtime Id-member graph type. */ value, Type type, GraphValidationState state,
        ValueValidation? validation, ValidationProfile profile, int depth)
    {
        if (CommunicationContracts.TryProject(value, out var surrogate) /* KLD0041: ManagedCode converters expose the actual selected producer surrogate for this runtime graph node. */)
        {
            return Visit(surrogate, state, null, profile, depth) /* KLD0041: The producer surrogate is traversed with its own Id-field nullability metadata. */;
        }
        ValidateCollection(value, type) /* KLD0041: The runtime closed collection type determines whether its default-state property must be inspected. */;
        var height = 0;
        if (value is IDictionary dictionary /* KLD0042: The runtime collection interface is validated before using its key/value enumeration contract. */)
        {
            var entries = dictionary.GetEnumerator() /* KLD0041: The closed dictionary types are runtime metadata; Entry avoids the boxed IEnumerator.Current wrapper. */;
            try
            {
                while (entries.MoveNext())
                {
                    var entry = entries.Entry;
                    height = Math.Max(height, Visit(entry.Key /* KLD0041: IDictionary exposes the current key as object; its requiredness comes from the owning member metadata. */, state, validation?.DictionaryKey, profile, depth) /* KLD0041: The current native dictionary key is validated with its own nullability contract. */);
                    height = Math.Max(height, Visit(entry.Value /* KLD0041: IDictionary exposes the current value as object; its requiredness comes from the owning member metadata. */, state, validation?.DictionaryValue, profile, depth) /* KLD0041: The current native dictionary value is validated after its key with the original traversal order. */);
                }
            }
            finally
            {
                (entries as IDisposable)?.Dispose();
            }
            return height;
        }
        if (value is IEnumerable sequence /* KLD0042: Runtime closed element types require the native heterogeneous enumerable interface at this graph boundary. */)
        {
            foreach (var item in sequence)
            {
                // Public JSON permits null reference elements; their owning validators preserve error precedence.
                if (item is not null || profile != ValidationProfile.PublicInputElements)
                {
                    height = Math.Max(height, Visit(item, state, validation?.Element ?? validation, profile, depth) /* KLD0041: This runtime enumerable element is validated with the owning element metadata and public-input profile. */);
                }
                else
                {
                    state.Charge();
                }
            }
            return height;
        }
        return VisitMembers(value, type, state, validation, profile, depth) /* KLD0041: Noncollection runtime values are inspected only through their serialized Id members. */;
    }

    private static bool IsScalar(Type type)
        => type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
            || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan)
            || type == typeof(Guid) || type == typeof(JsonElement) || type == typeof(ReadOnlyMemory<byte>)
            || type == typeof(Memory<byte>) || type == typeof(byte[]);

    private static int VisitMembers(object /* KLD0041: Reflection getters inspect the current runtime record whose Id-member metadata was selected above. */ value, Type type, GraphValidationState state,
        ValueValidation? validation, ValidationProfile profile, int depth)
    {
        var members = Members.GetOrAdd(type, GetMembers);
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
        {
            return Math.Max(Visit(members[0].Read /* KLD0041: This delegate is the reflected Key getter of the current closed KeyValuePair type. */(value) /* KLD0041: The native reflection getter returns the pair key through its object result ABI. */, state, validation?.DictionaryKey, profile, depth) /* KLD0041: The reflected pair key is checked against its distinct dictionary-key nullability contract. */,
                Visit(members[1].Read /* KLD0041: This delegate is the reflected Value getter of the current closed KeyValuePair type. */(value) /* KLD0041: The native reflection getter returns the pair value through its object result ABI. */, state, validation?.DictionaryValue, profile, depth) /* KLD0041: The reflected pair value is checked against its distinct dictionary-value nullability contract. */);
        }
        var height = 0;
        foreach (var member in members)
        {
            height = Math.Max(height, Visit(member.Read /* KLD0041: This cached delegate belongs to one serialized Id member on the current runtime type. */(value) /* KLD0041: Native field and property getters return this member value through their object result ABI. */, state, member.Value,
                member.PublicInput ? ValidationProfile.PublicInputElements : profile, depth) /* KLD0041: The reflected Id value retains its own nullability and public-input metadata for this traversal. */);
        }
        return height;
    }

    private static void ValidateCollection(object /* KLD0041: The existing boxed closed collection is the receiver required by its native reflection property getter. */ value, Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ImmutableArray<>)
            && CollectionDefaults.GetOrAdd(type, static current => current.GetProperty(nameof(ImmutableArray<int>.IsDefault))!).GetValue(value) /* KLD0041: ImmutableArray<T>.IsDefault is resolved from the runtime closed type; reflection returns its boolean through object. */ is true)
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
            return [new(type.GetProperty(nameof(KeyValuePair<int, int>.Key))!.GetValue, null) /* KLD0041: CLR reflection selects the key accessor of the actual runtime-closed KeyValuePair type. */,
                new(type.GetProperty(nameof(KeyValuePair<int, int>.Value))!.GetValue, null) /* KLD0041: CLR reflection selects the value accessor of the actual runtime-closed KeyValuePair type. */];
        }
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var property in current.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (property.IsDefined(typeof(Orleans.IdAttribute)))
                {
                    members.Add(new(property.GetValue, ValueValidation.Create(context.Create(property)),
                        property.IsDefined(typeof(PublicInputAttribute))) /* KLD0041: The reflected Id property accessor uses the CLR object input/output contract of its actual declaring member. */);
                }
            }
            foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.IsDefined(typeof(Orleans.IdAttribute)))
                {
                    members.Add(new(field.GetValue, ValueValidation.Create(context.Create(field))) /* KLD0041: The reflected Id field accessor uses the CLR object input/output contract of its actual declaring member. */);
                }
            }
        }
        return members.ToArray();
    }
}
