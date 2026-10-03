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

namespace KeyLoad.UnitTests;

// Official native writers intentionally author rejected field shapes inside the shared envelope.
internal static class ReplicaSecurityWireFixture
{
    private const string LegacyEscapedJson = "{\"id\":\"00000000-0000-0000-0000-000000000001\",\"kind\":\"Membership\",\"principalId\":\"http://voter-a:8080\",\"evaluatedAt\":\"1970-01-01T00:00:00Z\",\"payloadJson\":\"\\u0065\\u0033\\u0030\\u003D\"}";
    internal static byte[] LegacyEscapedOperation => Encoding.UTF8.GetBytes(LegacyEscapedJson);

    internal static byte[] Operation(ReplicatedOperation value, string shape)
        => Encode(value, new ReplicaSecurityOperationCodec(shape));

    internal static byte[] Append(AppendRequest value, bool duplicateOperation)
        => Encode(value, new ReplicaSecurityAppendCodec(duplicateOperation), builder =>
        {
            if (duplicateOperation)
            {
                builder.Services.AddSingleton(new ReplicaSecurityEntryCodec());
                builder.Configure(options => options.FieldCodecs.Add(typeof(ReplicaSecurityEntryCodec)));
            }
        });

    private static byte[] Encode<T, TCodec>(T value, TCodec codec, Action<global::Orleans.Serialization.ISerializerBuilder>? configure = null)
        where TCodec : class
    {
        var context = NativeSerializerProviders.CreateInspection(typeof(T), builder =>
        {
            builder.AddAssembly(typeof(ReplicaEntry).Assembly);
            builder.Services.AddSingleton(codec);
            builder.Configure(options => options.FieldCodecs.Add(typeof(TCodec)));
            configure?.Invoke(builder);
        });
        var body = context.Serializer.SerializeToArray(new NativePayload { Version = NativePayloadVersion.Current, Value = value });
        var bytes = new byte[checked(ReplicaProtocol.PayloadPrefixBytes + body.Length)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, ReplicaProtocol.PayloadMagic);
        body.CopyTo(bytes, ReplicaProtocol.PayloadPrefixBytes);
        return bytes;
    }

    internal static void Write<T, TWriter>(ref Writer<TWriter> writer, uint delta, T value) where TWriter : IBufferWriter<byte>
        => writer.Session.CodecProvider.GetCodec<T>().WriteField(ref writer, delta, typeof(T), value);
}

internal sealed class ReplicaSecurityOperationCodec(string shape) : IFieldCodec<ReplicatedOperation>
{
    public ReplicatedOperation ReadValue<TInput>(ref Reader<TInput> reader, Field field) => throw new NotSupportedException();

    public void WriteField<TWriter>(ref Writer<TWriter> writer, uint delta, [AllowNull] Type expectedType,
        [AllowNull] ReplicatedOperation value) where TWriter : IBufferWriter<byte>
    {
        if (ReferenceCodec.TryWriteReferenceField(ref writer, delta, expectedType, value))
        { return; }
        writer.WriteFieldHeader(delta, expectedType, typeof(ReplicatedOperation), WireType.TagDelimited);
        writer.WriteEndBase();
        if (shape == ReplicaSecurityFixture.MissingOperationFields)
        {
            ReplicaSecurityWireFixture.Write(ref writer, 1, value.Kind);
            writer.WriteEndObject();
            return;
        }
        ReplicaSecurityWireFixture.Write(ref writer, 0, value.Id);
        ReplicaSecurityWireFixture.Write(ref writer, 1, shape == ReplicaSecurityFixture.UnknownKind ? (OperationKind)int.MaxValue : value.Kind);
        if (shape == ReplicaSecurityFixture.DuplicateKind)
        { ReplicaSecurityWireFixture.Write(ref writer, 0, value.Kind); }
        ReplicaSecurityWireFixture.Write(ref writer, 1, value.PrincipalId);
        ReplicaSecurityWireFixture.Write(ref writer, 1, value.EvaluatedAt);
        if (shape == ReplicaSecurityFixture.InvalidPayload)
        { ReplicaSecurityWireFixture.Write(ref writer, 1, false); }
        else
        { ReplicaSecurityWireFixture.Write(ref writer, 1, value.PayloadJson); }
        ReplicaSecurityWireFixture.Write(ref writer, 1, value.NativePayload);
        if (shape == ReplicaSecurityFixture.UnknownField)
        { ReplicaSecurityWireFixture.Write(ref writer, 1, true); }
        writer.WriteEndObject();
    }
}

internal sealed class ReplicaSecurityAppendCodec(bool duplicateOperation) : IFieldCodec<AppendRequest>
{
    public AppendRequest ReadValue<TInput>(ref Reader<TInput> reader, Field field) => throw new NotSupportedException();

    public void WriteField<TWriter>(ref Writer<TWriter> writer, uint delta, [AllowNull] Type expectedType,
        [AllowNull] AppendRequest value) where TWriter : IBufferWriter<byte>
    {
        if (ReferenceCodec.TryWriteReferenceField(ref writer, delta, expectedType, value))
        { return; }
        writer.WriteFieldHeader(delta, expectedType, typeof(AppendRequest), WireType.TagDelimited);
        writer.WriteEndBase();
        ReplicaSecurityWireFixture.Write(ref writer, 0, value.LeaderId);
        ReplicaSecurityWireFixture.Write(ref writer, 1, value.Term);
        ReplicaSecurityWireFixture.Write(ref writer, 1, value.PreviousIndex);
        ReplicaSecurityWireFixture.Write(ref writer, 1, value.PreviousTerm);
        ReplicaSecurityWireFixture.Write(ref writer, 1, value.CommittedIndex);
        ReplicaSecurityWireFixture.Write(ref writer, 1, value.Entries);
        if (!duplicateOperation)
        { ReplicaSecurityWireFixture.Write(ref writer, 0, value.Entries); }
        writer.WriteEndObject();
    }
}

internal sealed class ReplicaSecurityEntryCodec : IFieldCodec<ReplicaEntry>
{
    public ReplicaEntry ReadValue<TInput>(ref Reader<TInput> reader, Field field) => throw new NotSupportedException();

    public void WriteField<TWriter>(ref Writer<TWriter> writer, uint delta, [AllowNull] Type expectedType,
        [AllowNull] ReplicaEntry value) where TWriter : IBufferWriter<byte>
    {
        if (ReferenceCodec.TryWriteReferenceField(ref writer, delta, expectedType, value))
        { return; }
        writer.WriteFieldHeader(delta, expectedType, typeof(ReplicaEntry), WireType.TagDelimited);
        writer.WriteEndBase();
        ReplicaSecurityWireFixture.Write(ref writer, 0, value.Index);
        ReplicaSecurityWireFixture.Write(ref writer, 1, value.Term);
        ReplicaSecurityWireFixture.Write<ReplicatedOperation?, TWriter>(ref writer, 1, null);
        ReplicaSecurityWireFixture.Write(ref writer, 0, value.Operation);
        writer.WriteEndObject();
    }
}
