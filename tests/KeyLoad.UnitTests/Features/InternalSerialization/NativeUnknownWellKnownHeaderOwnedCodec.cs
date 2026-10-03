using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using KeyLoad.Features.InternalSerialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

// An actual owned field codec raises a programming error after successful native preflight.
internal sealed class NativeUnknownWellKnownHeaderOwnedCodec(KeyNotFoundException error)
    : IFieldCodec<NativeUnknownWellKnownHeaderRecord>
{
    public NativeUnknownWellKnownHeaderRecord ReadValue<TInput>(ref Reader<TInput> reader, Field field) => throw error;

    public void WriteField<TWriter>(ref Writer<TWriter> writer, uint delta, [AllowNull] Type expectedType,
        [AllowNull] NativeUnknownWellKnownHeaderRecord value) where TWriter : IBufferWriter<byte>
        => NativeSerializerProviders.Get(typeof(NativeUnknownWellKnownHeaderRecord)).Sessions.CodecProvider
            .GetCodec<NativeUnknownWellKnownHeaderRecord>().WriteField(ref writer, delta, expectedType, value);
}
