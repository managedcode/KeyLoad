using KeyLoad.Features.InternalSerialization;
using KeyLoad.Orleans;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class McpNativeAuthenticationReader(bool enforceMcpBounds, IOptions<McpExecutionOptions> options, CancellationToken cancellationToken)
{
    private const uint FirstField = 0;
    private const uint NextField = 1;
    private const uint NativePayloadValueDelta = 1;
    // Explicit property Ids belong to one body scope after the empty constructor scope.
    private readonly McpAuthenticationProjection projection = new(enforceMcpBounds, options);
    private readonly McpNativeAuthenticationCollections collections = new(enforceMcpBounds: enforceMcpBounds, cancellationToken: cancellationToken, options: options);

    internal McpFrameShape Read<TInput>(ref Reader<TInput> reader)
    {
        const int EmptyRemaining = 0;

        cancellationToken.ThrowIfCancellationRequested();
        NativePayloadHeader.Validate(NativeFieldHeaderReader.Read(ref reader));
        var reference = ReferenceCodec.CreateRecordPlaceholder(reader.Session);
        var version = McpAuthenticationNativeFields.Scalar<uint, TInput>(ref reader, FirstField);
        if (version != NativePayloadVersion.Current)
        { throw Errors.Fail(ErrorCode.FormatUnsupported, NativePayloadVersion.UnsupportedVersion); }
        var field = McpAuthenticationNativeFields.Field(ref reader, NativePayloadValueDelta, typeof(GrainValue));
        McpAuthenticationNativeFields.Require(field.FieldType == typeof(GrainValue));
        var shape = Grain(ref reader, field);
        McpAuthenticationNativeFields.End(ref reader);
        McpAuthenticationNativeFields.Require(reader.Remaining == EmptyRemaining);
        McpAuthenticationNativeFields.Record(ref reader, reference, typeof(NativePayload), shape);
        cancellationToken.ThrowIfCancellationRequested();
        return shape;
    }

    private McpFrameShape Grain<TInput>(ref Reader<TInput> reader, Field field)
    {
        var reference = McpAuthenticationNativeFields.Begin(ref reader, field, typeof(GrainValue));
        McpAuthenticationNativeFields.EndBase(ref reader);
        var value = McpAuthenticationNativeFields.Field(ref reader, GrainNativeContracts.ValueField, typeof(PrincipalRecord));
        McpAuthenticationNativeFields.Require(value.FieldType == typeof(PrincipalRecord));
        var shape = Principal(ref reader, value);
        McpAuthenticationNativeFields.End(ref reader);
        McpAuthenticationNativeFields.Record(ref reader, reference, typeof(GrainValue), shape);
        return shape;
    }

    private McpFrameShape Principal<TInput>(ref Reader<TInput> reader, Field field)
    {
        var reference = McpAuthenticationNativeFields.Begin(ref reader, field, typeof(PrincipalRecord));
        McpAuthenticationNativeFields.EndBase(ref reader);
        var shape = projection.Object(McpAuthenticationProjection.PrincipalProperties);
        shape = String(ref reader, shape, FirstField);
        shape = String(ref reader, shape, NextField);
        shape = projection.Add(shape, collections.Array<ScopeGrant, TInput>(ref reader, NextField));
        shape = projection.Add(shape, collections.Array<string, TInput>(ref reader, NextField));
        shape = Scalar<bool, TInput>(ref reader, shape, NextField);
        shape = String(ref reader, shape, NextField, nullable: true);
        shape = projection.Add(shape, collections.Array<string, TInput>(ref reader, NextField));
        shape = Scalar<bool, TInput>(ref reader, shape, NextField);
        shape = Scalar<bool, TInput>(ref reader, shape, NextField);
        shape = Expires(ref reader, shape);
        shape = Scalar<long, TInput>(ref reader, shape, NextField);
        McpAuthenticationNativeFields.End(ref reader);
        McpAuthenticationNativeFields.Record(ref reader, reference, typeof(PrincipalRecord), shape);
        return shape;
    }

    private McpFrameShape String<TInput>(ref Reader<TInput> reader, McpFrameShape shape, uint delta, bool nullable = false)
    {
        var field = McpAuthenticationNativeFields.Field(ref reader, delta, typeof(string));
        return projection.Add(shape, collections.String(ref reader, field, nullable));
    }

    private McpFrameShape Scalar<T, TInput>(ref Reader<TInput> reader, McpFrameShape shape, uint delta)
    {
        _ = McpAuthenticationNativeFields.Scalar<T, TInput>(ref reader, delta);
        return projection.Add(shape, McpAuthenticationProjection.Scalar);
    }

    private McpFrameShape Expires<TInput>(ref Reader<TInput> reader, McpFrameShape shape)
    {
        var field = McpAuthenticationNativeFields.Field(ref reader, NextField, typeof(DateTimeOffset));
        if (field.IsReference)
        { _ = reader.Session.CodecProvider.GetCodec<DateTimeOffset?>().ReadValue(ref reader, field); }
        else
        {
            // The native DateTimeOffset codec permits unknown subfields. Admission requires its
            // exact two-scalar shape so an opaque nested graph cannot bypass the depth ceiling.
            field.EnsureWireTypeTagDelimited();
            ReferenceCodec.MarkValueField(reader.Session);
            var date = McpAuthenticationNativeFields.Scalar<DateTime, TInput>(ref reader, FirstField);
            var offset = McpAuthenticationNativeFields.Scalar<TimeSpan, TInput>(ref reader, NextField);
            McpAuthenticationNativeFields.End(ref reader);
            _ = new DateTimeOffset(date, offset);
        }
        return projection.Add(shape, McpAuthenticationProjection.Scalar);
    }
}
