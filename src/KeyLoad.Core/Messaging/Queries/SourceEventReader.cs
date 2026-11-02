using System.Text.Json;
using KeyLoad.Storage;

namespace KeyLoad.Core.Messaging;

/// <summary>Decodes persisted source records directly from their gate-scoped bytes.</summary>
internal static class SourceEventReader
{
    private const string EventUnavailableMessage = "The requested event is unavailable.";
    private const string EventRecordCorruptMessage = "The persisted event record does not match its source position.";

    /// <summary>Reads and validates one topic or stream record without an intermediate value copy.</summary>
    /// <param name="view">The storage view valid for the current read or transaction action.</param>
    /// <param name="source">Expected source identity.</param>
    /// <param name="position">Expected one-based source position.</param>
    /// <param name="observer">Optional execution operation accounting before raw bytes are decoded.</param>
    /// <returns>The owned decoded event record.</returns>
    /// <exception cref="KeyLoadException">The record is unavailable or its persisted identity is corrupt.</exception>
    public static SourceEventRecord Read(IKeyValueView view, EventSourceRef source, long position,
        StorageReadObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(view) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(source) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        SourceEventRecord? result = null;
        var key = EventSources.SourceKey(source, position);
        var found = view.ReadValue(key, bytes => result = Decode(bytes, source, position), observer);
        if (!found)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, EventUnavailableMessage);
        }
        return result ?? throw Errors.Fail(ErrorCode.Corruption, EventRecordCorruptMessage);
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
        var record = OrleansSerialization.Deserialize<SourceEventRecord>(bytes);
        if (record.Source != source || record.Position != position)
        {
            throw Errors.Fail(ErrorCode.Corruption, EventRecordCorruptMessage);
        }
        return record;
    }

    private static SourceEventRecord DecodeStream(ReadOnlySpan<byte> bytes, EventSourceRef source, long position)
    {
        var record = OrleansSerialization.Deserialize<EventRecord>(bytes);
        var expectedStream = new StreamRef(source.Partition, source.Resource, source.StreamId!, source.Generation);
        if (record.Stream != expectedStream || record.Revision != position)
        {
            throw Errors.Fail(ErrorCode.Corruption, EventRecordCorruptMessage);
        }
        return new(source, record.Revision, record.EventSequence, record.Data, record.RecordedAt);
    }
}
