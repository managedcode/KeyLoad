using System.Buffers.Binary;
using System.Text;
using KeyLoad.Features.InternalSerialization;
using KeyLoad.Replication;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

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
        {
            return ReplicaProtocolCodec.Serialize(value with
            {
                Entries = [new(1, 3, null)]
            });
        }
        if (shape == ReadRoundGuardPayloads.InvalidTerm)
        {
            return ReplicaProtocolCodec.Serialize(value with
            {
                Term = 0
            });
        }
        if (shape == ReadRoundGuardPayloads.InvalidPrevious)
        {
            return ReplicaProtocolCodec.Serialize(value with
            {
                PreviousIndex = 1
            });
        }
        var bytes = shape is ReadRoundGuardPayloads.CaseMutated or ReadRoundGuardPayloads.Trailing
            ? ReplicaProtocolCodec.Serialize(value) : Encode(value, shape);
        if (shape == ReadRoundGuardPayloads.Trailing)
        {
            return [.. bytes, TrailingByte];
        }
        if (shape == ReadRoundGuardPayloads.CaseMutated)
        {
            var aliasOffset = bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(AppendAlias));
            if (aliasOffset < 0)
            {
                throw new InvalidOperationException(AliasNotEncoded);
            }
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
