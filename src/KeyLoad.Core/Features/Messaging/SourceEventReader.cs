using System.Text.Json;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.Messaging;

/// <summary>Decodes persisted source records directly from their gate-scoped bytes.</summary>
internal static class SourceEventReader
{
    private const string EventUnavailableMessage = "The requested event is unavailable.";
    private const string EventRecordCorruptMessage = "The persisted event record does not match its source position.";
    private const string TopicEventSpace = "topic-event";
    private const string StreamEventSpace = "event";

    /// <summary>Reads and validates one topic or stream record without an intermediate value copy.</summary>
    /// <param name="view">The storage view valid for the current read or transaction action.</param>
    /// <param name="source">Expected source identity.</param>
    /// <param name="position">Expected one-based source position.</param>
    /// <returns>The owned decoded event record.</returns>
    /// <exception cref="KeyLoadException">The record is unavailable or its persisted identity is corrupt.</exception>
    public static SourceEventRecord Read(IKeyValueView view, EventSourceRef source, long position)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(source);
        SourceEventRecord? result = null;
        var key = EventKey(source, position);
        var found = view.ReadValue(key, bytes => result = Decode(bytes, source, position));
        if (!found)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, EventUnavailableMessage);
        }
        return result ?? throw Errors.Fail(ErrorCode.Corruption, EventRecordCorruptMessage);
    }

    private static byte[] EventKey(EventSourceRef source, long position)
    {
        object?[] suffix = source.Kind == EventSourceKind.Topic
            ? [source.Resource, source.Generation, position]
            : [source.Resource, source.StreamId, source.Generation, position];
        return KeySpace.Partition(source.Kind == EventSourceKind.Topic ? TopicEventSpace : StreamEventSpace, source.Partition, suffix);
    }

    private static SourceEventRecord Decode(ReadOnlySpan<byte> bytes, EventSourceRef source, long position)
    {
        try
        {
            return source.Kind == EventSourceKind.Topic
                ? DecodeTopic(bytes, source, position)
                : DecodeStream(bytes, source, position);
        }
        catch (JsonException)
        {
            throw Errors.Fail(ErrorCode.Corruption, EventRecordCorruptMessage);
        }
    }

    private static SourceEventRecord DecodeTopic(ReadOnlySpan<byte> bytes, EventSourceRef source, long position)
    {
        var record = JsonDefaults.Deserialize<SourceEventRecord>(bytes);
        if (record.Source != source || record.Position != position)
        {
            throw Errors.Fail(ErrorCode.Corruption, EventRecordCorruptMessage);
        }
        return record;
    }

    private static SourceEventRecord DecodeStream(ReadOnlySpan<byte> bytes, EventSourceRef source, long position)
    {
        var record = JsonDefaults.Deserialize<EventRecord>(bytes);
        var expectedStream = new StreamRef(source.Partition, source.Resource, source.StreamId!, source.Generation);
        if (record.Stream != expectedStream || record.Revision != position)
        {
            throw Errors.Fail(ErrorCode.Corruption, EventRecordCorruptMessage);
        }
        return new(source, record.Revision, record.EventSequence, record.Data, record.RecordedAt);
    }
}
