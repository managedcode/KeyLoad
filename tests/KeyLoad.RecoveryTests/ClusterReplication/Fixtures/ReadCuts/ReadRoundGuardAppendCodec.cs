using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using KeyLoad.Replication;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.RecoveryTests;

internal sealed class ReadRoundGuardAppendCodec(string shape) : IFieldCodec<AppendRequest>
{
    public AppendRequest ReadValue<TInput>(ref Reader<TInput> reader, Field field) => throw new NotSupportedException();

    public void WriteField<TWriter>(ref Writer<TWriter> writer, uint delta, [AllowNull] Type expectedType,
        [AllowNull] AppendRequest value) where TWriter : IBufferWriter<byte>
    {
        if (ReferenceCodec.TryWriteReferenceField(ref writer, delta, expectedType, value))
        {
            return;
        }
        writer.WriteFieldHeader(delta, expectedType, typeof(AppendRequest), WireType.TagDelimited);
        writer.WriteEndBase();
        if (shape != ReadRoundGuardPayloads.MissingLeader)
        {
            Write(ref writer, 0, value.LeaderId);
        }
        WriteScalars(ref writer, value);
        if (shape != ReadRoundGuardPayloads.MissingEntries)
        {
            Write(ref writer, shape == ReadRoundGuardPayloads.MissingCommittedIndex ? 2U : 1U,
                shape == ReadRoundGuardPayloads.NullEntries ? default : value.Entries);
        }
        if (shape == ReadRoundGuardPayloads.Duplicate)
        {
            Write(ref writer, 0, value.Entries);
        }
        if (shape == ReadRoundGuardPayloads.Unknown)
        {
            Write(ref writer, 1, true);
        }
        writer.WriteEndObject();
    }

    private void WriteScalars<TWriter>(ref Writer<TWriter> writer, AppendRequest value) where TWriter : IBufferWriter<byte>
    {
        if (shape != ReadRoundGuardPayloads.MissingTerm)
        {
            Write(ref writer, 1, value.Term);
        }
        if (shape != ReadRoundGuardPayloads.MissingPreviousIndex)
        {
            Write(ref writer, shape == ReadRoundGuardPayloads.MissingTerm ? 2U : 1U, value.PreviousIndex);
        }
        if (shape != ReadRoundGuardPayloads.MissingPreviousTerm)
        {
            Write(ref writer, shape == ReadRoundGuardPayloads.MissingPreviousIndex ? 2U : 1U, value.PreviousTerm);
        }
        if (shape != ReadRoundGuardPayloads.MissingCommittedIndex)
        {
            Write(ref writer, shape == ReadRoundGuardPayloads.MissingPreviousTerm ? 2U : 1U, value.CommittedIndex);
        }
    }

    private static void Write<T, TWriter>(ref Writer<TWriter> writer, uint delta, T value) where TWriter : IBufferWriter<byte>
        => writer.Session.CodecProvider.GetCodec<T>().WriteField(ref writer, delta, typeof(T), value);
}
