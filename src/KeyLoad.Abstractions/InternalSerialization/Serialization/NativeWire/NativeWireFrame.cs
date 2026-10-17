using System.Collections.Immutable;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.Features.InternalSerialization;

internal sealed class NativeWireFrame
{
    private const uint ImmutableValuesId = 0;
    private const uint ImmutableValueComparerId = 1;
    private const int ImmutableValueTypeArgumentIndex = 1;
    private readonly Type? type;
    private readonly Type[] arguments;
    private readonly bool collection;
    private readonly bool dictionary;
    private readonly bool immutableDictionary;
    private readonly bool verifiedImmutableValues;
    private bool immutableValues;
    private bool immutableValueComparer;
    private uint id;
    private int scope;
    private uint? count;
    private ulong elements;
    private bool comparer;

    internal bool IsImmutableDictionaryValues => immutableDictionary && immutableValues && id == ImmutableValuesId;

    internal NativeWireFrame(Type? type, bool verifiedImmutableValues = false)
    {
        this.type = NativeWireSchema.Normalize(type);
        this.verifiedImmutableValues = verifiedImmutableValues;
        arguments = this.type?.GenericTypeArguments ?? [];
        dictionary = this.type?.IsGenericType == true && this.type.GetGenericTypeDefinition() == typeof(Dictionary<,>);
        immutableDictionary = this.type?.IsGenericType == true && this.type.GetGenericTypeDefinition() == typeof(ImmutableDictionary<,>);
        collection = dictionary || this.type?.IsArray == true || IsSequence(this.type);
    }

    internal Type? ReadMember<TInput>(ref Reader<TInput> reader, Field field, out bool consumed)
    {
        id = checked(id + field.FieldIdDelta);
        consumed = false;
        if (immutableDictionary)
        {
            return ReadImmutableMember(ref reader, field, out consumed);
        }
        if (!collection)
        {
            return NativeWireSchema.Member(type, scope, id);
        }
        var countId = dictionary ? NativeWireIdentities.DictionaryCountId : NativeWireIdentities.SequenceCountId;
        if (dictionary && id == NativeWireIdentities.DictionaryComparerId)
        {
            NativeWireCheck.Require(!comparer && count is null);
            comparer = true;
            if (verifiedImmutableValues)
            {
                return ReadVerifiedComparer(ref reader, field, arguments[NativeWireIdentities.KeyTypeArgumentIndex], out consumed);
            }
            return typeof(IEqualityComparer<>).MakeGenericType(arguments[NativeWireIdentities.KeyTypeArgumentIndex]);
        }
        if (id == countId)
        {
            ReadCount(ref reader, field);
            consumed = true;
            return typeof(uint);
        }
        NativeWireCheck.Require(id == countId + NativeWireIdentities.CollectionItemDelta && count is not null);
        elements++;
        NativeWireCheck.Require(elements <= (ulong)count!.Value * (dictionary ? NativeWireIdentities.DictionaryFieldsPerItem : NativeWireIdentities.SequenceFieldsPerItem));
        return dictionary ? arguments[(int)((elements - NativeWireIdentities.FirstElementOrdinal) % NativeWireIdentities.DictionaryArgumentCount)] : type!.IsArray ? type.GetElementType() : arguments[NativeWireIdentities.KeyTypeArgumentIndex];
    }

    internal void EndBase()
    {
        NativeWireCheck.Require(!collection && !immutableDictionary);
        scope++;
        id = NativeWireIdentities.FirstFieldId;
    }

    internal void Complete()
    {
        if (immutableDictionary)
        {
            NativeWireCheck.Require(immutableValues);
        }
        if (collection)
        {
            NativeWireCheck.Require(elements == (ulong)(count ?? NativeWireIdentities.EmptyCollectionCount) * (dictionary ? NativeWireIdentities.DictionaryFieldsPerItem : NativeWireIdentities.SequenceFieldsPerItem));
        }
    }

    private Type? ReadImmutableMember<TInput>(ref Reader<TInput> reader, Field field, out bool consumed)
    {
        consumed = false;
        if (id == ImmutableValuesId)
        {
            // The pinned codec creates a fresh Dictionary for every surrogate Values field.
            // Requiring its actual tag prevents null/opaque/reference aliases before allocation.
            NativeWireCheck.Require(!immutableValues && !immutableValueComparer && field.WireType == WireType.TagDelimited);
            immutableValues = true;
            return typeof(Dictionary<,>).MakeGenericType(arguments);
        }
        NativeWireCheck.Require(id == ImmutableValueComparerId && immutableValues && !immutableValueComparer);
        immutableValueComparer = true;
        return ReadVerifiedComparer(ref reader, field, arguments[ImmutableValueTypeArgumentIndex], out consumed);
    }

    private static Type? ReadVerifiedComparer<TInput>(ref Reader<TInput> reader, Field field, Type valueType, out bool consumed)
    {
        consumed = false;
        if (field.IsReference)
        {
            // Only the codec's default null comparer is admitted as a reference.
            NativeWireCheck.Require(field.FieldType is null && reader.ReadVarUInt32() == NativeWireIdentities.NullReferenceId);
            consumed = true;
            return null;
        }
        NativeWireCheck.Require(valueType == typeof(string) && field.WireType == WireType.TagDelimited
            && field.FieldType == typeof(WellKnownStringComparerCodec));
        return typeof(IEqualityComparer<>).MakeGenericType(valueType);
    }

    private void ReadCount<TInput>(ref Reader<TInput> reader, Field field)
    {
        NativeWireCheck.Require(count is null && elements == NativeWireIdentities.EmptyElementCount && !field.IsReference
            && (field.FieldType is null || field.FieldType == typeof(uint)));
        count = UInt32Codec.ReadValue(ref reader, field);
        // Each item requires at least a native header and one byte of scalar/reference data.
        // Dictionary allocations additionally require both key and value fields per entry.
        NativeWireCheck.Require(count <= int.MaxValue && count <= (ulong)reader.Remaining / (dictionary ? NativeWireIdentities.MinimumDictionaryEntryBytes : NativeWireIdentities.MinimumSequenceItemBytes));
    }

    private static bool IsSequence(Type? type)
    {
        if (type?.IsGenericType != true)
        {
            return false;
        }
        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(List<>) || definition == typeof(Memory<>) || definition == typeof(ReadOnlyMemory<>);
    }
}
