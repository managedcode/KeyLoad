using System.Text;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.Features.InternalSerialization;

internal static class WireWalk
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static void Validate<TInput>(ref Reader<TInput> reader, Field root)
    {
        var frames = new Stack<WireFrame>();
        // Scalar/value fields and references advance execution reference IDs without becoming targets.
        var references = new List<Type?> { null, typeof(RuntimePayload), null };
        Consume(ref reader, root, root.FieldType, frames, references);
        while (frames.Count > WireIdentities.EmptyFrameCount)
        {
            var field = FieldHeaderReader.Read(ref reader);
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
            WireCheck.Require(field.HasFieldId);
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
        Stack<WireFrame> frames, List<Type?> references)
    {
        var actual = WireSchema.Normalize(field.FieldType ?? expected);
        WireSupported.Require(field.FieldType);
        if (field.IsReference)
        {
            ConsumeReference(ref reader, expected, references);
            return;
        }
        WireSupported.Require(actual);
        if (field.FieldType is not null && expected is not null)
        {
            WireCheck.Require(WireSchema.Compatible(expected, field.FieldType));
        }
        references.Add(ReferenceTarget(field, actual));
        if (field.WireType == WireType.TagDelimited)
        {
            WireCheck.Require(frames.Count + WireIdentities.EnvelopeAndValueDepth <= SerializationLimits.WireDepth);
            var immutableValues = frames.TryPeek(out var parent) && parent.IsImmutableDictionaryValues;
            frames.Push(new WireFrame(actual, immutableValues));
            return;
        }
        ConsumeScalar(ref reader, field, actual);
    }

    private static Type? ReferenceTarget(Field field, Type? actual)
    {
        actual = WireSchema.ReferenceType(actual);
        if (field.WireType is WireType.VarInt or WireType.Fixed32 or WireType.Fixed64
            || actual?.IsValueType == true || actual == typeof(WellKnownStringComparerCodec))
        {
            return null;
        }
        // Homogeneous encoding rejects deferred typed decoding of ambiguous implicit unknown fields:
        // a later typed reference must never turn an uninspected tag into a collection allocator.
        return actual ?? typeof(OpaqueReference);
    }

    private static void ConsumeReference<TInput>(ref Reader<TInput> reader, Type? expected, List<Type?> references)
    {
        var target = reader.ReadVarUInt32();
        WireCheck.Require(target == WireIdentities.NullReferenceId || target < references.Count && references[(int)target] is not null);
        if (target > WireIdentities.NullReferenceId && expected is not null)
        {
            WireCheck.Require(Nullable.GetUnderlyingType(expected) is null);
            WireCheck.Require(references[(int)target] != typeof(OpaqueReference)
                && WireSchema.Compatible(WireSchema.ReferenceType(expected)!, references[(int)target]!));
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
                reader.EnsureAvailable(WireIdentities.Fixed32Bytes);
                reader.Skip(WireIdentities.Fixed32Bytes);
                break;
            case WireType.Fixed64:
                reader.EnsureAvailable(WireIdentities.Fixed64Bytes);
                reader.Skip(WireIdentities.Fixed64Bytes);
                break;
            case WireType.LengthPrefixed:
                ConsumeBytes(ref reader, type);
                break;
            default:
                WireCheck.Require(false);
                break;
        }
    }

    private static void ConsumeBytes<TInput>(ref Reader<TInput> reader, Type? type)
    {
        var length = reader.ReadVarUInt32();
        reader.EnsureAvailable(length);
        if (type == typeof(string))
        {
            WireCheck.Require(reader.TryReadBytes(checked((int)length), out var utf8));
            _ = StrictUtf8.GetCharCount(utf8);
            return;
        }
        reader.Skip(length);
    }

    private readonly record struct OpaqueReference;
}
