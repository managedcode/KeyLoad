using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using KeyLoad.Features.InternalSerialization;
using KeyLoad.Replication;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.RecoveryTests;

internal static class ReplicaMaximumPayload
{
    internal const string Quotes = "\\\"";
    internal const string Unicode = "Ж";
    internal const string ValidFields = "valid-fields";
    internal const string UnknownField = "unknown-field";
    internal const string DuplicateField = "duplicate-kind";
    private const string Prefix = " { \"value\": \"";
    private const string Suffix = "\" }\n";
    private const string IdField = "id";
    private const string KindField = "kind";
    private const string PrincipalField = "principalId";
    private const string EvaluatedAtField = "evaluatedAt";
    private const string PayloadField = "payloadJson";

    internal static string Create(string token, int maxBytes)
    {
        var available = maxBytes - Encoding.UTF8.GetByteCount(Prefix + Suffix);
        var count = available / Encoding.UTF8.GetByteCount(token);
        var payload = Prefix + string.Concat(Enumerable.Repeat(token, count)) + Suffix;
        return payload + new string(' ', maxBytes - Encoding.UTF8.GetByteCount(payload));
    }

    internal static byte[] LegacyJson(ReplicatedOperation value)
    {
        var output = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(output);
        writer.WriteStartObject();
        writer.WriteString(IdField, value.Id);
        writer.WriteString(KindField, value.Kind.ToString());
        writer.WriteString(PrincipalField, value.PrincipalId);
        writer.WriteString(EvaluatedAtField, value.EvaluatedAt);
        writer.WriteBase64String(PayloadField, Encoding.UTF8.GetBytes(value.PayloadJson));
        writer.WriteEndObject();
        writer.Flush();
        return output.WrittenSpan.ToArray();
    }

    internal static byte[] Malformed(ReplicatedOperation value, string shape)
    {
        var context = NativeSerializerProviders.CreateInspection(typeof(ReplicatedOperation), builder =>
        {
            builder.AddAssembly(typeof(ReplicaEntry).Assembly);
            builder.Services.AddSingleton(new ReplicaMaximumOperationCodec(shape));
            builder.Configure(options => options.FieldCodecs.Add(typeof(ReplicaMaximumOperationCodec)));
        });
        var body = context.Serializer.SerializeToArray(new NativePayload { Version = NativePayloadVersion.Current, Value = value });
        var bytes = new byte[checked(ReplicaProtocol.PayloadPrefixBytes + body.Length)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, ReplicaProtocol.PayloadMagic);
        body.CopyTo(bytes, ReplicaProtocol.PayloadPrefixBytes);
        return bytes;
    }
}

// Fixture-only native writer preserves the existing 8 MiB identity while authoring exact field defects.
internal sealed class ReplicaMaximumOperationCodec(string shape) : IFieldCodec<ReplicatedOperation>
{
    private readonly IFieldCodec<ReadOnlyMemory<byte>> bytesCodec = new ReadOnlyMemoryOfByteCodec();

    public ReplicatedOperation ReadValue<TInput>(ref Reader<TInput> reader, Field field) => throw new NotSupportedException();

    public void WriteField<TWriter>(ref Writer<TWriter> writer, uint delta, [AllowNull] Type expectedType,
        [AllowNull] ReplicatedOperation value) where TWriter : IBufferWriter<byte>
    {
        if (ReferenceCodec.TryWriteReferenceField(ref writer, delta, expectedType, value))
        { return; }
        writer.WriteFieldHeader(delta, expectedType, typeof(ReplicatedOperation), WireType.TagDelimited);
        writer.WriteEndBase();
        Write(ref writer, 0, value.Id);
        Write(ref writer, 1, value.Kind);
        if (shape == ReplicaMaximumPayload.DuplicateField)
        { Write(ref writer, 0, value.Kind); }
        Write(ref writer, 1, value.PrincipalId);
        Write(ref writer, 1, value.EvaluatedAt);
        Write(ref writer, 1, value.PayloadJson);
        bytesCodec.WriteField(ref writer, 1, typeof(ReadOnlyMemory<byte>), value.NativePayload);
        if (shape == ReplicaMaximumPayload.UnknownField)
        { Write(ref writer, 1, true); }
        writer.WriteEndObject();
    }

    private static void Write<T, TWriter>(ref Writer<TWriter> writer, uint delta, T value) where TWriter : IBufferWriter<byte>
        => writer.Session.CodecProvider.GetCodec<T>().WriteField(ref writer, delta, typeof(T), value);
}
