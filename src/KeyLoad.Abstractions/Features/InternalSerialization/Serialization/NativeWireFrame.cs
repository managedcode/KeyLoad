using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.Features.InternalSerialization;

internal sealed class NativeWireFrame
{
    private readonly Type? type;
    private readonly Type[] arguments;
    private readonly bool collection;
    private readonly bool dictionary;
    private uint id;
    private int scope;
    private uint? count;
    private ulong elements;
    private bool comparer;

    internal NativeWireFrame(Type? type)
    {
        this.type = NativeWireSchema.Normalize(type);
        arguments = this.type?.GenericTypeArguments ?? [];
        dictionary = this.type?.IsGenericType == true && this.type.GetGenericTypeDefinition() == typeof(Dictionary<,>);
        collection = dictionary || this.type?.IsArray == true || IsSequence(this.type);
    }

    internal Type? ReadMember<TInput>(ref Reader<TInput> reader, Field field, out bool consumed)
    {
        id = checked(id + field.FieldIdDelta);
        consumed = false;
        if (!collection)
        {
            return NativeWireSchema.Member(type, scope, id);
        }
        var countId = dictionary ? NativeWireIdentities.DictionaryCountId : NativeWireIdentities.SequenceCountId;
        if (dictionary && id == NativeWireIdentities.DictionaryComparerId)
        {
            NativeWireCheck.Require(!comparer && count is null);
            comparer = true;
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
        NativeWireCheck.Require(!collection);
        scope++;
        id = NativeWireIdentities.FirstFieldId;
    }

    internal void Complete()
    {
        if (collection)
        {
            NativeWireCheck.Require(elements == (ulong)(count ?? NativeWireIdentities.EmptyCollectionCount) * (dictionary ? NativeWireIdentities.DictionaryFieldsPerItem : NativeWireIdentities.SequenceFieldsPerItem));
        }
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

internal static class NativeWireCheck
{
    internal static void Require(bool condition)
    {
        if (!condition)
        {
            throw Errors.Fail(ErrorCode.Corruption, NativePayloadVersion.InvalidPayload);
        }
    }
}
