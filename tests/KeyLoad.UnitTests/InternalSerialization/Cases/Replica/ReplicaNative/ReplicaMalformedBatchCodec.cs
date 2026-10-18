using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.UnitTests;

internal sealed class ReplicaMalformedBatchCodec(ReplicaMalformedArrayShape shape) : IFieldCodec<ReplicaEntryBatch>
{
    public ReplicaEntryBatch ReadValue<TInput>(ref Reader<TInput> reader, Field field) => throw new NotSupportedException();

    public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint delta,
        [AllowNull] Type expectedType, [AllowNull] ReplicaEntryBatch value) where TBufferWriter : IBufferWriter<byte>
    {
        if (ReferenceCodec.TryWriteReferenceField(ref writer, delta, expectedType, value))
        {
            return;
        }
        writer.WriteFieldHeader(delta, expectedType, typeof(ReplicaEntryBatch), WireType.TagDelimited);
        writer.WriteEndBase();
        ReferenceCodec.MarkValueField(writer.Session);
        writer.WriteFieldHeader(0, typeof(ImmutableArray<ReplicaEntry>), typeof(ImmutableArray<ReplicaEntry>), WireType.TagDelimited);
        if (shape == ReplicaMalformedArrayShape.Null)
        {
            writer.Session.CodecProvider.GetCodec<ReplicaEntry[]>().WriteField(ref writer, 0, typeof(ReplicaEntry[]), null!);
        }
        else
        {
            WriteArray(ref writer, value.Entries);
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private void WriteArray<TBufferWriter>(ref Writer<TBufferWriter> writer, ImmutableArray<ReplicaEntry> entries)
        where TBufferWriter : IBufferWriter<byte>
    {
        var values = entries.ToArray();
        if (ReferenceCodec.TryWriteReferenceField(ref writer, 0, typeof(ReplicaEntry[]), values))
        {
            return;
        }
        writer.WriteFieldHeader(0, typeof(ReplicaEntry[]), typeof(ReplicaEntry[]), WireType.TagDelimited);
        var declared = shape == ReplicaMalformedArrayShape.Underfilled ? values.Length : values.Length - 1;
        UInt32Codec.WriteField(ref writer, 0, checked((uint)declared));
        var actual = shape == ReplicaMalformedArrayShape.Underfilled ? values.Length - 1 : values.Length;
        var element = writer.Session.CodecProvider.GetCodec<ReplicaEntry>();
        for (var index = 0; index < actual; index++)
        {
            element.WriteField(ref writer, index == 0 ? 1U : 0U, typeof(ReplicaEntry), values[index]);
        }
        writer.WriteEndObject();
    }
}
