using System.Buffers;
using System.Collections.Immutable;
using KeyLoad.Server;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class McpAuthenticationMalformedArrays
{
    internal static void Grants<TBuffer>(ref Writer<TBuffer> writer, ImmutableArray<ScopeGrant> values,
        McpAuthenticationArrayFault? fault) where TBuffer : IBufferWriter<byte>
    {
        var items = values.ToArray();
        Start(ref writer, items);
        var count = fault == McpAuthenticationArrayFault.TooManyGrantProperties
            ? checked((uint)(McpFramingProtocol.MaximumProperties / McpAuthenticationProjection.GrantProperties) + 1)
            : checked((uint)items.Length);
        if (count != 0)
        {
            UInt32Codec.WriteField(ref writer, 0, count);
        }
        for (var index = 0; index < items.Length; index++)
        {
            writer.Session.CodecProvider.GetCodec<ScopeGrant>().WriteField(ref writer, index == 0 ? 1u : 0u, typeof(ScopeGrant), items[index]);
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    internal static void Strings<TBuffer>(ref Writer<TBuffer> writer, ImmutableArray<string> values,
        PrincipalRecord principal, McpAuthenticationArrayFault? fault) where TBuffer : IBufferWriter<byte>
    {
        var items = values.ToArray();
        Start(ref writer, items);
        var count = fault switch
        {
            McpAuthenticationArrayFault.TooManyItems => checked((uint)McpFramingProtocol.MaximumTokens + 1),
            McpAuthenticationArrayFault.Underfilled => checked((uint)items.Length + 1),
            _ => checked((uint)items.Length)
        };
        if (count != 0)
        {
            UInt32Codec.WriteField(ref writer, 0, count);
        }
        for (var index = 0; index < items.Length; index++)
        {
            Item(ref writer, index == 0 ? 1u : 0u, items[index], principal, fault);
        }
        if (fault == McpAuthenticationArrayFault.Overfilled)
        {
            StringCodec.WriteField(ref writer, 0, McpAuthenticationMalformedFixture.Item);
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void Start<T, TBuffer>(ref Writer<TBuffer> writer, T[] values) where TBuffer : IBufferWriter<byte>
    {
        ReferenceCodec.MarkValueField(writer.Session);
        writer.WriteFieldHeader(1, typeof(ImmutableArray<T>), typeof(ImmutableArray<T>), WireType.TagDelimited);
        McpAuthenticationMalformedFixture.Begin(ref writer, 0, typeof(T[]), values);
    }

    private static void Item<TBuffer>(ref Writer<TBuffer> writer, uint delta, string value,
        PrincipalRecord principal, McpAuthenticationArrayFault? fault) where TBuffer : IBufferWriter<byte>
    {
        if (fault == McpAuthenticationArrayFault.WrongReferenceType)
        {
            // Omitted type metadata reaches the actual reference-target compatibility check.
            if (!ReferenceCodec.TryWriteReferenceField(ref writer, delta, typeof(ScopeGrant), principal.Grants[0]))
            {
                throw new InvalidOperationException("The genuine completed grant reference was not registered.");
            }
        }
        else if (fault == McpAuthenticationArrayFault.WrongType)
        {
            writer.Session.CodecProvider.GetCodec<int>().WriteField(ref writer, delta, typeof(string), 1);
        }
        else
        {
            StringCodec.WriteField(ref writer, delta, fault == McpAuthenticationArrayFault.NullElement ? null : value);
        }
    }
}
