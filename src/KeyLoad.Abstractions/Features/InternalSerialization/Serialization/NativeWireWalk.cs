using System.Text;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.Features.InternalSerialization;

internal static class NativeWireWalk
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static void Validate<TInput>(ref Reader<TInput> reader, Field root)
    {
        var frames = new Stack<NativeWireFrame>();
        // Scalar/value fields and references advance native reference IDs without becoming targets.
        var references = new List<Type?> { null, typeof(NativePayload), null };
        Consume(ref reader, root, root.FieldType, frames, references);
        while (frames.Count > NativeWireIdentities.EmptyFrameCount)
        {
            var field = NativeFieldHeaderReader.Read(ref reader);
            var frame = frames.Peek();
            if (field.IsEndObject)
            {
                frame.Complete();
                frames.Pop();
                continue;
            }
            if (field.IsEndBaseFields)
            {
                frame.EndBase();
                continue;
            }
            NativeWireCheck.Require(field.HasFieldId);
            var expected = frame.ReadMember(ref reader, field, out var consumed);
            if (consumed)
            {
                references.Add(null);
                continue;
            }
            Consume(ref reader, field, expected, frames, references);
        }
    }

    private static void Consume<TInput>(ref Reader<TInput> reader, Field field, Type? expected,
        Stack<NativeWireFrame> frames, List<Type?> references)
    {
        var actual = NativeWireSchema.Normalize(field.FieldType ?? expected);
        NativeWireSupported.Require(field.FieldType);
        if (field.IsReference)
        {
            ConsumeReference(ref reader, expected, references);
            return;
        }
        NativeWireSupported.Require(actual);
        if (field.FieldType is not null && expected is not null)
        {
            NativeWireCheck.Require(NativeWireSchema.Compatible(expected, field.FieldType));
        }
        references.Add(ReferenceTarget(field, actual));
        if (field.WireType == WireType.TagDelimited)
        {
            NativeWireCheck.Require(frames.Count + NativeWireIdentities.EnvelopeAndValueDepth <= NativeSerializationLimits.WireDepth);
            frames.Push(new NativeWireFrame(actual));
            return;
        }
        ConsumeScalar(ref reader, field, actual);
    }

    private static Type? ReferenceTarget(Field field, Type? actual)
    {
        actual = NativeWireSchema.ReferenceType(actual);
        if (field.WireType is WireType.VarInt or WireType.Fixed32 or WireType.Fixed64
            || actual?.IsValueType == true || actual == typeof(WellKnownStringComparerCodec))
        {
            return null;
        }
        // Homogeneous v2 rejects deferred typed decoding of ambiguous implicit unknown fields:
        // a later typed reference must never turn an uninspected tag into a collection allocator.
        return actual ?? typeof(OpaqueReference);
    }

    private static void ConsumeReference<TInput>(ref Reader<TInput> reader, Type? expected, List<Type?> references)
    {
        var target = reader.ReadVarUInt32();
        NativeWireCheck.Require(target == NativeWireIdentities.NullReferenceId || target < references.Count && references[(int)target] is not null);
        if (target > NativeWireIdentities.NullReferenceId && expected is not null)
        {
            NativeWireCheck.Require(Nullable.GetUnderlyingType(expected) is null);
            NativeWireCheck.Require(references[(int)target] != typeof(OpaqueReference)
                && NativeWireSchema.Compatible(NativeWireSchema.ReferenceType(expected)!, references[(int)target]!));
        }
        references.Add(null);
    }

    private static void ConsumeScalar<TInput>(ref Reader<TInput> reader, Field field, Type? type)
    {
        switch (field.WireType)
        {
            case WireType.VarInt:
                _ = reader.ReadVarUInt64();
                break;
            case WireType.Fixed32:
                reader.EnsureAvailable(NativeWireIdentities.Fixed32Bytes);
                reader.Skip(NativeWireIdentities.Fixed32Bytes);
                break;
            case WireType.Fixed64:
                reader.EnsureAvailable(NativeWireIdentities.Fixed64Bytes);
                reader.Skip(NativeWireIdentities.Fixed64Bytes);
                break;
            case WireType.LengthPrefixed:
                ConsumeBytes(ref reader, type);
                break;
            default:
                NativeWireCheck.Require(false);
                break;
        }
    }

    private static void ConsumeBytes<TInput>(ref Reader<TInput> reader, Type? type)
    {
        var length = reader.ReadVarUInt32();
        reader.EnsureAvailable(length);
        if (type == typeof(string))
        {
            NativeWireCheck.Require(reader.TryReadBytes(checked((int)length), out var utf8));
            _ = StrictUtf8.GetCharCount(utf8);
            return;
        }
        reader.Skip(length);
    }

    private readonly record struct OpaqueReference;
}
