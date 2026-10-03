using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using KeyLoad.Replication;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.UnitTests;

internal sealed class NativeReadProbeCodec(NativeReadProbeFault fault) : IFieldCodec<AppendRequest>
{
    private const uint LeaderField = 0;
    private const uint TermField = 1;
    private const uint PreviousIndexField = 2;
    private const uint PreviousTermField = 3;
    private const uint CommittedIndexField = 4;
    private const uint EntriesField = 5;
    private const uint UnknownField = 6;

    public AppendRequest ReadValue<TInput>(ref Reader<TInput> reader, Field field) => throw new NotSupportedException();

    public void WriteField<TWriter>(ref Writer<TWriter> writer, uint delta, [AllowNull] Type expectedType,
        [AllowNull] AppendRequest value) where TWriter : IBufferWriter<byte>
    {
        if (ReferenceCodec.TryWriteReferenceField(ref writer, delta, expectedType, value))
        { return; }
        writer.WriteFieldHeader(delta, expectedType, typeof(AppendRequest), WireType.TagDelimited);
        writer.WriteEndBase();
        NativeReadProbeProducer.Write(ref writer, LeaderField, value.LeaderId);
        var previous = LeaderField;
        Header(ref writer, ref previous, TermField, fault == NativeReadProbeFault.ZeroTerm ? 0 : value.Term,
            NativeReadProbeFault.MissingTerm);
        Header(ref writer, ref previous, PreviousIndexField, fault == NativeReadProbeFault.NegativePreviousIndex ? -1 : value.PreviousIndex,
            NativeReadProbeFault.MissingPreviousIndex);
        Header(ref writer, ref previous, PreviousTermField, fault == NativeReadProbeFault.ConflictingPreviousTerm ? 1 : value.PreviousTerm,
            NativeReadProbeFault.MissingPreviousTerm);
        Header(ref writer, ref previous, CommittedIndexField, fault == NativeReadProbeFault.NegativeCommittedIndex ? -1 : value.CommittedIndex,
            NativeReadProbeFault.MissingCommittedIndex);
        Entries(ref writer, previous, value.Entries);
        writer.WriteEndObject();
    }

    private void Header<TWriter>(ref Writer<TWriter> writer, ref uint previous, uint fieldId, long value,
        NativeReadProbeFault missing) where TWriter : IBufferWriter<byte>
    {
        if (fault == missing)
        { return; }
        NativeReadProbeProducer.Write(ref writer, fieldId - previous, value);
        previous = fieldId;
    }

    private void Entries<TWriter>(ref Writer<TWriter> writer, uint previous, ImmutableArray<ReplicaEntry> entries)
        where TWriter : IBufferWriter<byte>
    {
        if (fault == NativeReadProbeFault.MissingEntries)
        {
            NativeReadProbeProducer.Write(ref writer, UnknownField - previous, entries);
            return;
        }
        NativeReadProbeProducer.Write(ref writer, EntriesField - previous,
            fault == NativeReadProbeFault.NullEntries ? default : entries);
        if (fault == NativeReadProbeFault.DuplicateEntries)
        { NativeReadProbeProducer.Write(ref writer, 0, entries); }
        if (fault == NativeReadProbeFault.UnknownEntries)
        { NativeReadProbeProducer.Write(ref writer, UnknownField - EntriesField, entries); }
    }
}
