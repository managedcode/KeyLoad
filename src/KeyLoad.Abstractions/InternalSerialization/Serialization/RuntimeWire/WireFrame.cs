using System.Collections.Immutable;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.Features.InternalSerialization;

internal sealed class WireFrame
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

    internal WireFrame(Type? type, bool verifiedImmutableValues = false)
    {
        this.type = WireSchema.Normalize(type);
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
            return WireSchema.Member(type, scope, id);
        }
        var countId = dictionary ? WireIdentities.DictionaryCountId : WireIdentities.SequenceCountId;
        if (dictionary && id == WireIdentities.DictionaryComparerId)
        {
            WireCheck.Require(!comparer && count is null);
            comparer = true;
            if (verifiedImmutableValues)
            {
                return ReadVerifiedComparer(ref reader, field, arguments[WireIdentities.KeyTypeArgumentIndex], out consumed);
            }
            return typeof(IEqualityComparer<>).MakeGenericType(arguments[WireIdentities.KeyTypeArgumentIndex]);
        }
        if (id == countId)
        {
            ReadCount(ref reader, field);
            consumed = true;
            return typeof(uint);
        }
        WireCheck.Require(id == countId + WireIdentities.CollectionItemDelta && count is not null);
        elements++;
        WireCheck.Require(elements <= (ulong)count!.Value * (dictionary ? WireIdentities.DictionaryFieldsPerItem : WireIdentities.SequenceFieldsPerItem));
        return dictionary ? arguments[(int)((elements - WireIdentities.FirstElementOrdinal) % WireIdentities.DictionaryArgumentCount)] : type!.IsArray ? type.GetElementType() : arguments[WireIdentities.KeyTypeArgumentIndex];
    }

    internal void EndBase()
    {
        WireCheck.Require(!collection && !immutableDictionary);
        scope++;
        id = WireIdentities.FirstFieldId;
    }

    internal void Complete()
    {
        if (immutableDictionary)
        {
            WireCheck.Require(immutableValues);
        }
        if (collection)
        {
            WireCheck.Require(elements == (ulong)(count ?? WireIdentities.EmptyCollectionCount) * (dictionary ? WireIdentities.DictionaryFieldsPerItem : WireIdentities.SequenceFieldsPerItem));
        }
    }

    private Type? ReadImmutableMember<TInput>(ref Reader<TInput> reader, Field field, out bool consumed)
    {
        consumed = false;
        if (id == ImmutableValuesId)
        {
            // The pinned codec creates a fresh Dictionary for every surrogate Values field.
            // Requiring its actual tag prevents null/opaque/reference aliases before allocation.
            WireCheck.Require(!immutableValues && !immutableValueComparer && field.WireType == WireType.TagDelimited);
            immutableValues = true;
            return typeof(Dictionary<,>).MakeGenericType(arguments);
        }
        WireCheck.Require(id == ImmutableValueComparerId && immutableValues && !immutableValueComparer);
        immutableValueComparer = true;
        return ReadVerifiedComparer(ref reader, field, arguments[ImmutableValueTypeArgumentIndex], out consumed);
    }

    private static Type? ReadVerifiedComparer<TInput>(ref Reader<TInput> reader, Field field, Type valueType, out bool consumed)
    {
        consumed = false;
        if (field.IsReference)
        {
            // Only the codec's default null comparer is admitted as a reference.
            WireCheck.Require(field.FieldType is null && reader.ReadVarUInt32() == WireIdentities.NullReferenceId);
            consumed = true;
            return null;
        }
        WireCheck.Require(valueType == typeof(string) && field.WireType == WireType.TagDelimited
            && field.FieldType == typeof(WellKnownStringComparerCodec));
        return typeof(IEqualityComparer<>).MakeGenericType(valueType);
    }

    private void ReadCount<TInput>(ref Reader<TInput> reader, Field field)
    {
        WireCheck.Require(count is null && elements == WireIdentities.EmptyElementCount && !field.IsReference
            && (field.FieldType is null || field.FieldType == typeof(uint)));
        count = UInt32Codec.ReadValue(ref reader, field);
        // Each item requires at least a execution header and one byte of scalar/reference data.
        // Dictionary allocations additionally require both key and value fields per entry.
        WireCheck.Require(count <= int.MaxValue && count <= (ulong)reader.Remaining / (dictionary ? WireIdentities.MinimumDictionaryEntryBytes : WireIdentities.MinimumSequenceItemBytes));
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
