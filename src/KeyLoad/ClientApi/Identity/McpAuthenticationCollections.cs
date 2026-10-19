using System.Collections.Immutable;
using System.Text;
using KeyLoad.Features.InternalSerialization;
using Microsoft.Extensions.Options;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.Server;

internal sealed class McpAuthenticationCollections(bool enforceMcpBounds, IOptions<McpExecutionOptions> options, CancellationToken cancellationToken)
{
    private const uint FirstField = 0;
    private const uint NextField = 1;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly McpAuthenticationProjection projection = new(enforceMcpBounds, options);

    internal McpFrameShape Array<T, TInput>(ref Reader<TInput> reader, uint delta) where T : class
    {
        var field = McpAuthenticationFields.Field(ref reader, delta, typeof(ImmutableArray<T>));
        McpAuthenticationFields.Require(!field.IsReference && field.WireType == WireType.TagDelimited);
        ReferenceCodec.MarkValueField(reader.Session);
        var values = McpAuthenticationFields.Field(ref reader, FirstField, typeof(T[]));
        var shape = Values<T, TInput>(ref reader, values);
        McpAuthenticationFields.End(ref reader);
        return shape;
    }

    private McpFrameShape Values<T, TInput>(ref Reader<TInput> reader, Field field) where T : class
    {
        const int CountValidationBoundary = 0;
        const int IndexInitialValue = 0;
        const int EmptyIndex = 0;

        cancellationToken.ThrowIfCancellationRequested();
        if (field.IsReference)
        {
            var referenced = McpAuthenticationFields.Reference(ref reader, typeof(T[]));
            McpAuthenticationFields.Require(referenced is not null);
            return referenced!.Shape;
        }
        var reference = McpAuthenticationFields.Begin(ref reader, field, typeof(T[]));
        var shape = McpAuthenticationProjection.Array;
        var countField = FieldHeaderReader.Read(ref reader);
        if (countField.IsEndObject)
        {
            McpAuthenticationFields.Record(ref reader, reference, typeof(T[]), shape);
            return shape;
        }
        McpAuthenticationFields.Require(countField.HasFieldId && countField.FieldIdDelta == FirstField
            && (countField.FieldType is null || countField.FieldType == typeof(uint)));
        var count = UInt32Codec.ReadValue(ref reader, countField);
        McpAuthenticationFields.Require(count > CountValidationBoundary);
        projection.RequireCount<T>(count);
        reader.EnsureAvailable(count);
        for (uint index = IndexInitialValue; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = McpAuthenticationFields.Field(ref reader, index == EmptyIndex ? NextField : FirstField, typeof(T));
            var child = typeof(T) == typeof(string) ? String(ref reader, item, nullable: false) : Grant(ref reader, item);
            shape = projection.Add(shape, child);
        }
        McpAuthenticationFields.End(ref reader);
        McpAuthenticationFields.Record(ref reader, reference, typeof(T[]), shape);
        return shape;
    }

    internal McpFrameShape String<TInput>(ref Reader<TInput> reader, Field field, bool nullable)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (field.IsReference)
        {
            var referenced = McpAuthenticationFields.Reference(ref reader, typeof(string));
            McpAuthenticationFields.Require(nullable || referenced is not null);
            return McpAuthenticationProjection.Scalar;
        }
        field.EnsureWireType(WireType.LengthPrefixed);
        var length = reader.ReadVarUInt32();
        reader.EnsureAvailable(length);
        McpAuthenticationFields.Require(reader.TryReadBytes(checked((int)length), out var utf8));
        _ = StrictUtf8.GetCharCount(utf8);
        ReferenceCodec.RecordObject(reader.Session, new McpAuthenticationReference(typeof(string), McpAuthenticationProjection.Scalar));
        return McpAuthenticationProjection.Scalar;
    }

    private McpFrameShape Grant<TInput>(ref Reader<TInput> reader, Field field)
    {
        if (field.IsReference)
        {
            var referenced = McpAuthenticationFields.Reference(ref reader, typeof(ScopeGrant));
            McpAuthenticationFields.Require(referenced is not null);
            return referenced!.Shape;
        }
        var reference = McpAuthenticationFields.Begin(ref reader, field, typeof(ScopeGrant));
        McpAuthenticationFields.EndBase(ref reader);
        var shape = projection.Object(McpAuthenticationProjection.GrantProperties);
        shape = projection.Add(shape, String(ref reader,
            McpAuthenticationFields.Field(ref reader, FirstField, typeof(string)), nullable: false));
        shape = projection.Add(shape, String(ref reader,
            McpAuthenticationFields.Field(ref reader, NextField, typeof(string)), nullable: false));
        _ = McpAuthenticationFields.Scalar<Capability, TInput>(ref reader, NextField);
        shape = projection.Add(shape, McpAuthenticationProjection.Scalar);
        McpAuthenticationFields.End(ref reader);
        McpAuthenticationFields.Record(ref reader, reference, typeof(ScopeGrant), shape);
        return shape;
    }
}
