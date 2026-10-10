using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using global::Orleans.Serialization;

namespace KeyLoad.Orleans;

internal static class EventFeedPubSubRowEncoding
{
    private const string ETagFormat = "N";

    internal static EventFeedPubSubRow Create<T>(IServiceProvider originalServices, GrainId grainId,
        T state, IOptions<DatabaseLimits> options, IOptions<GrainRoutingOptions> routing,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(originalServices);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(routing);
        if (state is null) { throw Errors.Fail(ErrorCode.Validation, EventFeedPubSubProtocol.Invalid); }
        options.Value.Validate();
        var maximum = checked((int)Math.Min(options.Value.MaxBatchBytes, options.Value.MaxQueryReadBytes));
        var etag = Guid.NewGuid().ToString(ETagFormat);
        var keySerializer = originalServices.GetRequiredService<Serializer<GrainId>>();
        var stringSerializer = originalServices.GetRequiredService<Serializer<string>>();
        var serializer = originalServices.GetRequiredService<Serializer<T>>();
        var keyBytes = Measure(keySerializer, grainId, maximum, routing, cancellationToken);
        var nameBytes = Measure(stringSerializer, EventFeedPubSubProtocol.StateName, maximum, routing, cancellationToken);
        var etagBytes = Measure(stringSerializer, etag, maximum, routing, cancellationToken);
        var valueBytes = Measure(serializer, state, maximum, routing, cancellationToken);
        var bytes = checked(keyBytes + nameBytes + etagBytes + valueBytes);
        if (bytes > maximum) { throw Errors.Fail(ErrorCode.ResourceExhausted, EventFeedPubSubProtocol.Capacity); }
        cancellationToken.ThrowIfCancellationRequested();
        var encodedKey = Encode(keySerializer, grainId, keyBytes);
        var encodedName = Encode(stringSerializer, EventFeedPubSubProtocol.StateName, nameBytes);
        var encodedEtag = Encode(stringSerializer, etag, etagBytes);
        var value = Encode(serializer, state, valueBytes);
        bytes = checked(encodedKey.LongLength + encodedName.LongLength + encodedEtag.LongLength + value.LongLength);
        var detachedKey = keySerializer.Deserialize(encodedKey);
        return new(detachedKey, typeof(T), value, etag, bytes);
    }

    private static byte[] Encode<T>(Serializer<T> serializer, T value, long measuredBytes)
    {
        var originalBuffer = serializer.SerializeToArray(value);
        if (originalBuffer.LongLength != measuredBytes)
        { throw Errors.Fail(ErrorCode.Corruption, EventFeedPubSubProtocol.Invalid); }
        return originalBuffer;
    }

    private static long Measure<T>(Serializer<T> serializer, T value, int maximum,
        IOptions<GrainRoutingOptions> routing, CancellationToken cancellationToken)
    {
        using var counter = new EventFeedPubSubCountingWriter(maximum, routing, cancellationToken);
        serializer.Serialize(value, counter);
        return counter.Length;
    }
}
