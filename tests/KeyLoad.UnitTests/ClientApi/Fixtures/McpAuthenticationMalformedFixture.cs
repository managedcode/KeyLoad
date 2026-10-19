using System.Buffers;
using KeyLoad.Clustering;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

// Author real array boundaries because generated surrogate Values bypasses provider overrides.
internal static class McpAuthenticationMalformedFixture
{
    internal const string Item = "execution-auth-array-item";

    internal static byte[] Array(PrincipalRecord principal, McpAuthenticationArrayFault fault) => Write(principal, fault);
    internal static byte[] Valid(PrincipalRecord principal) => Write(principal, null);

    internal static byte[] DefaultArray(PrincipalRecord principal)
        => OrleansSerialization.Serializer.SerializeToArray(new RuntimePayload
        {
            Version = PayloadVersion.Current,
            Value = new GrainValue(principal with { FieldGrants = default })
        });

    internal static byte[] DefaultTeams(PrincipalRecord principal)
        => OrleansSerialization.Serializer.SerializeToArray(new RuntimePayload
        {
            Version = PayloadVersion.Current,
            Value = new GrainValue(principal with { Teams = default })
        });

    private static byte[] Write(PrincipalRecord principal, McpAuthenticationArrayFault? fault)
    {
        using var session = OrleansSerialization.Serializer.SessionPool.GetSession();
        var writer = Writer.CreatePooled(session);
        try
        {
            var grain = new GrainValue(principal);
            Begin(ref writer, 0, typeof(RuntimePayload), new RuntimePayload { Version = PayloadVersion.Current, Value = grain });
            UInt32Codec.WriteField(ref writer, 0, PayloadVersion.Current);
            Begin(ref writer, 1, typeof(object), grain);
            writer.WriteEndBase();
            Begin(ref writer, 0, typeof(object), principal);
            writer.WriteEndBase();
            WritePrincipal(ref writer, principal, fault);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.Commit();
            return writer.Output.ToArray();
        }
        finally
        {
            writer.Dispose();
        }
    }

    private static void WritePrincipal<TBuffer>(ref Writer<TBuffer> writer, PrincipalRecord value,
        McpAuthenticationArrayFault? fault) where TBuffer : IBufferWriter<byte>
    {
        StringCodec.WriteField(ref writer, 0, value.Id);
        StringCodec.WriteField(ref writer, 1, value.TenantId);
        McpAuthenticationMalformedArrays.Grants(ref writer, value.Grants, fault);
        McpAuthenticationMalformedArrays.Strings(ref writer, value.FieldGrants, value, fault);
        Scalar(ref writer, value.ClusterAdministrator);
        StringCodec.WriteField(ref writer, 1, value.OwnerId);
        Scalar(ref writer, value.Projects);
        Scalar(ref writer, value.RestrictRows);
        Scalar(ref writer, value.Revoked);
        Scalar(ref writer, value.ExpiresAt);
        Scalar(ref writer, value.PolicyEpoch);
        Scalar(ref writer, value.Teams);
    }

    internal static void Begin<TBuffer>(ref Writer<TBuffer> writer, uint delta, Type expected, object value)
        where TBuffer : IBufferWriter<byte>
    {
        if (ReferenceCodec.TryWriteReferenceField(ref writer, delta, expected, value))
        {
            throw new InvalidOperationException("A new fixture object was unexpectedly already registered.");
        }
        writer.WriteFieldHeader(delta, expected, value.GetType(), WireType.TagDelimited);
    }

    private static void Scalar<T, TBuffer>(ref Writer<TBuffer> writer, T value) where TBuffer : IBufferWriter<byte>
        => writer.Session.CodecProvider.GetCodec<T>().WriteField(ref writer, 1, typeof(T), value);
}
