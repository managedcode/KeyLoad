using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using KeyLoad.Features.InternalSerialization;
using KeyLoad.Orleans;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal enum McpAuthenticationArrayFault { TooManyItems, TooManyGrantProperties, Underfilled, Overfilled, WrongType, WrongReferenceType, NullElement }

// Writer-only malformed fixture: the shared native envelope and generated owning records remain
// authoritative. Only the actual official array wire boundary is authored independently.
internal static class McpAuthenticationMalformedFixture
{
    internal static byte[] Array(PrincipalRecord principal, McpAuthenticationArrayFault fault)
    {
        var context = NativeSerializerProviders.CreateInspection(typeof(GrainValue), builder =>
        {
            builder.Services.AddSingleton(new MalformedAuthenticationArrayCodec(principal, fault));
            builder.Configure(options => options.FieldCodecs.Add(typeof(MalformedAuthenticationArrayCodec)));
            if (fault == McpAuthenticationArrayFault.TooManyGrantProperties)
            {
                builder.Services.AddSingleton(new MalformedAuthenticationGrantArrayCodec());
                builder.Configure(options => options.FieldCodecs.Add(typeof(MalformedAuthenticationGrantArrayCodec)));
            }
        });
        return context.Serializer.SerializeToArray(new NativePayload { Version = NativePayloadVersion.Current, Value = new GrainValue(principal) });
    }

    internal static byte[] DefaultArray(PrincipalRecord principal)
    {
        var context = NativeSerializerProviders.Get(typeof(GrainValue));
        return context.Serializer.SerializeToArray(new NativePayload
        {
            Version = NativePayloadVersion.Current,
            Value = new GrainValue(principal with { FieldGrants = default })
        });
    }
}

internal sealed class MalformedAuthenticationGrantArrayCodec : IFieldCodec<ScopeGrant[]>
{
    private const uint FirstField = 0;
    private const uint ExtraItem = 1;

    public ScopeGrant[] ReadValue<TInput>(ref Reader<TInput> reader, Field field) => throw new NotSupportedException();

    public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint delta,
        [AllowNull] Type expectedType, [AllowNull] ScopeGrant[] value) where TBufferWriter : IBufferWriter<byte>
    {
        if (ReferenceCodec.TryWriteReferenceField(ref writer, delta, expectedType, value))
        { return; }
        writer.WriteFieldHeader(delta, expectedType, typeof(ScopeGrant[]), WireType.TagDelimited);
        var count = checked((uint)(McpFramingProtocol.MaximumProperties / McpAuthenticationProjection.GrantProperties) + ExtraItem);
        UInt32Codec.WriteField(ref writer, FirstField, count);
        writer.WriteEndObject();
    }
}

internal sealed class MalformedAuthenticationArrayCodec(PrincipalRecord principal, McpAuthenticationArrayFault fault) : IFieldCodec<string[]>
{
    private const uint FirstField = 0;
    private const uint ItemField = 1;
    private const uint OneItem = 1;
    private const uint TwoItems = 2;
    private const string Item = "native-auth-array-item";
    private const int WrongScalar = 1;
    private const string MissingReference = "The genuine principal reference was not registered.";

    public string[] ReadValue<TInput>(ref Reader<TInput> reader, Field field) => throw new NotSupportedException();

    public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint delta,
        [AllowNull] Type expectedType, [AllowNull] string[] value) where TBufferWriter : IBufferWriter<byte>
    {
        if (ReferenceCodec.TryWriteReferenceField(ref writer, delta, expectedType, value))
        { return; }
        writer.WriteFieldHeader(delta, expectedType, typeof(string[]), WireType.TagDelimited);
        var count = fault switch
        {
            McpAuthenticationArrayFault.TooManyItems => checked((uint)McpFramingProtocol.MaximumTokens + OneItem),
            McpAuthenticationArrayFault.Underfilled => TwoItems,
            _ => OneItem
        };
        UInt32Codec.WriteField(ref writer, FirstField, count);
        WriteItem(ref writer);
        if (fault == McpAuthenticationArrayFault.Overfilled)
        { StringCodec.WriteField(ref writer, FirstField, Item); }
        writer.WriteEndObject();
    }

    private void WriteItem<TBufferWriter>(ref Writer<TBufferWriter> writer) where TBufferWriter : IBufferWriter<byte>
    {
        if (fault == McpAuthenticationArrayFault.WrongReferenceType)
        {
            if (!ReferenceCodec.TryWriteReferenceField(ref writer, ItemField, typeof(string), principal))
            { throw new InvalidOperationException(MissingReference); }
        }
        else if (fault == McpAuthenticationArrayFault.WrongType)
        { writer.Session.CodecProvider.GetCodec<int>().WriteField(ref writer, ItemField, typeof(string), WrongScalar); }
        else
        { StringCodec.WriteField(ref writer, ItemField, fault == McpAuthenticationArrayFault.NullElement ? null : Item); }
    }
}
