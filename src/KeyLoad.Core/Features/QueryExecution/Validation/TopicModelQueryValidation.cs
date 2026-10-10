using System.Text.Json;

namespace KeyLoad.Core.Features.QueryExecution;

internal static class TopicModelQueryValidation
{
    private const long EmptyTail = 0L;
    private const long FirstPosition = 1L;
    private const int InitialSchemaVersion = 1;
    private const ulong InclusivePosition = 1UL;
    private const ulong EmptyCount = 0UL;
    private const string Corrupt = "The retained topic source is inconsistent.";
    private const string ScanExceeded = "The model query scan exceeds its configured candidate budget.";

    internal static void RequireHead(EventSourceHead head, int maximumScanRecords)
    {
        if (head.TailPosition < EmptyTail || head.FirstAvailablePosition < FirstPosition
            || head.TailPosition < long.MaxValue && head.FirstAvailablePosition > head.TailPosition + FirstPosition)
        { throw Errors.Fail(ErrorCode.Corruption, Corrupt); }
        var count = head.TailPosition < head.FirstAvailablePosition ? EmptyCount
            : (ulong)head.TailPosition - (ulong)head.FirstAvailablePosition + InclusivePosition;
        if (count > (ulong)maximumScanRecords)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, ScanExceeded); }
    }

    internal static void Accept(PartitionRef partition, ResourceDefinition resource,
        SourceEventRecord record, Action<DocumentRecord> accept)
    {
        if (record.Data is null || record.Data.SchemaVersion < InitialSchemaVersion
            || !ModelQueryReadRows.IsIdentifier(record.Data.EventId)
            || !ModelQueryReadRows.IsIdentifier(record.Data.EventType))
        { throw Errors.Fail(ErrorCode.Corruption, Corrupt); }
        using var payload = ModelQueryReadRows.ParseObject(record.Data.PayloadJson, Corrupt);
        using var headers = ModelQueryReadRows.ParseObject(record.Data.HeadersJson, Corrupt);
        var json = JsonSerializer.Serialize(new
        {
            eventId = record.Data.EventId,
            eventType = record.Data.EventType,
            schemaVersion = record.Data.SchemaVersion,
            position = record.Position,
            eventSequence = record.EventSequence,
            recordedAt = record.RecordedAt,
            payload = payload.RootElement,
            headers = headers.RootElement
        }, JsonDefaults.Options);
        accept(new(new(partition, resource.Name, record.Data.EventId), record.Position, json,
            new RowAccess(), record.RecordedAt));
    }
}
