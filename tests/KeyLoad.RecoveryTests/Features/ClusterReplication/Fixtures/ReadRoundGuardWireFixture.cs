using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using KeyLoad.Features.InternalSerialization;
using KeyLoad.Replication;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.RecoveryTests;

internal static class ReadRoundGuardWireFixture
{
    private const string NonemptyControl = "nonempty";
    private const string AliasNotEncoded = "The native append fixture must encode its registered alias.";
    private const string AppendAlias = "keyload.replica.append-request.v2";
    private const byte CaseBit = 32;
    private const byte TrailingByte = 0;

    internal static byte[] Probe(string shape)
    {
        var value = new AppendRequest(ReadRoundStoredCluster.VoterA, 3, 0, 0, 0, []);
        if (shape == ReadRoundGuardPayloads.Nonempty)
        { return ReplicaProtocolCodec.Serialize(value with { Entries = [new(1, 3, null)] }); }
        if (shape == ReadRoundGuardPayloads.InvalidTerm)
        { return ReplicaProtocolCodec.Serialize(value with { Term = 0 }); }
        if (shape == ReadRoundGuardPayloads.InvalidPrevious)
        { return ReplicaProtocolCodec.Serialize(value with { PreviousIndex = 1 }); }
        var bytes = shape is ReadRoundGuardPayloads.CaseMutated or ReadRoundGuardPayloads.Trailing
            ? ReplicaProtocolCodec.Serialize(value) : Encode(value, shape);
        if (shape == ReadRoundGuardPayloads.Trailing)
        { return [.. bytes, TrailingByte]; }
        if (shape == ReadRoundGuardPayloads.CaseMutated)
        {
            var aliasOffset = bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(AppendAlias));
            if (aliasOffset < 0)
            { throw new InvalidOperationException(AliasNotEncoded); }
            bytes[aliasOffset] ^= CaseBit;
        }
        return bytes;
    }

    internal static byte[] Control(string shape) => shape switch
    {
        ReadRoundGuardPayloads.NonemptyString => ReplicaProtocolCodec.Serialize(NonemptyControl),
        ReadRoundGuardPayloads.Null => NullControl(),
        ReadRoundGuardPayloads.Object => ReplicaProtocolCodec.Serialize(new AppendReply(1, true, 1, 2)),
        _ => [.. ReplicaProtocolCodec.Serialize(string.Empty), TrailingByte]
    };

    private static byte[] NullControl()
    {
        var context = NativeSerializerProviders.CreateInspection(typeof(string));
        return Frame(context.Serializer.SerializeToArray(new NativePayload { Version = NativePayloadVersion.Current, Value = null }));
    }

    private static byte[] Encode(AppendRequest value, string shape)
    {
        var context = NativeSerializerProviders.CreateInspection(typeof(AppendRequest), builder =>
        {
            builder.AddAssembly(typeof(ReplicaEntry).Assembly);
            builder.Services.AddSingleton(new ReadRoundGuardAppendCodec(shape));
            builder.Configure(options => options.FieldCodecs.Add(typeof(ReadRoundGuardAppendCodec)));
        });
        return Frame(context.Serializer.SerializeToArray(new NativePayload { Version = NativePayloadVersion.Current, Value = value }));
    }

    private static byte[] Frame(byte[] body)
    {
        var bytes = new byte[checked(ReplicaProtocol.PayloadPrefixBytes + body.Length)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, ReplicaProtocol.PayloadMagic);
        body.CopyTo(bytes, ReplicaProtocol.PayloadPrefixBytes);
        return bytes;
    }
}

// Real official writers deliberately omit, duplicate or mistype one owned native field.
internal sealed class ReadRoundGuardAppendCodec(string shape) : IFieldCodec<AppendRequest>
{
    public AppendRequest ReadValue<TInput>(ref Reader<TInput> reader, Field field) => throw new NotSupportedException();

    public void WriteField<TWriter>(ref Writer<TWriter> writer, uint delta, [AllowNull] Type expectedType,
        [AllowNull] AppendRequest value) where TWriter : IBufferWriter<byte>
    {
        if (ReferenceCodec.TryWriteReferenceField(ref writer, delta, expectedType, value))
        { return; }
        writer.WriteFieldHeader(delta, expectedType, typeof(AppendRequest), WireType.TagDelimited);
        writer.WriteEndBase();
        if (shape != ReadRoundGuardPayloads.MissingLeader)
        { Write(ref writer, 0, value.LeaderId); }
        WriteScalars(ref writer, value);
        if (shape != ReadRoundGuardPayloads.MissingEntries)
        {
            Write(ref writer, shape == ReadRoundGuardPayloads.MissingCommittedIndex ? 2U : 1U,
                shape == ReadRoundGuardPayloads.NullEntries ? default : value.Entries);
        }
        if (shape == ReadRoundGuardPayloads.Duplicate)
        { Write(ref writer, 0, value.Entries); }
        if (shape == ReadRoundGuardPayloads.Unknown)
        { Write(ref writer, 1, true); }
        writer.WriteEndObject();
    }

    private void WriteScalars<TWriter>(ref Writer<TWriter> writer, AppendRequest value) where TWriter : IBufferWriter<byte>
    {
        if (shape != ReadRoundGuardPayloads.MissingTerm)
        { Write(ref writer, 1, value.Term); }
        if (shape != ReadRoundGuardPayloads.MissingPreviousIndex)
        { Write(ref writer, shape == ReadRoundGuardPayloads.MissingTerm ? 2U : 1U, value.PreviousIndex); }
        if (shape != ReadRoundGuardPayloads.MissingPreviousTerm)
        { Write(ref writer, shape == ReadRoundGuardPayloads.MissingPreviousIndex ? 2U : 1U, value.PreviousTerm); }
        if (shape != ReadRoundGuardPayloads.MissingCommittedIndex)
        { Write(ref writer, shape == ReadRoundGuardPayloads.MissingPreviousTerm ? 2U : 1U, value.CommittedIndex); }
    }

    private static void Write<T, TWriter>(ref Writer<TWriter> writer, uint delta, T value) where TWriter : IBufferWriter<byte>
        => writer.Session.CodecProvider.GetCodec<T>().WriteField(ref writer, delta, typeof(T), value);
}
