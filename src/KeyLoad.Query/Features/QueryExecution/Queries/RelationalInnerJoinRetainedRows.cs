namespace KeyLoad.Query;

internal static class RelationalInnerJoinRetainedRows
{
    private const string RetainedRowCorrupt = "The retained relational join source is inconsistent.";

    internal static global::System.Text.Json.JsonDocument ParseRetainedJson(string json)
    {
        try
        {
            return global::System.Text.Json.JsonDocument.Parse(json);
        }
        catch (global::System.Text.Json.JsonException)
        {
            throw Errors.Fail(ErrorCode.Corruption, RetainedRowCorrupt);
        }
    }

    internal static void ValidatePrimaryIdentity(global::System.Text.Json.JsonElement json, string primaryKey, string id)
    {
        if (json.ValueKind != global::System.Text.Json.JsonValueKind.Object || !json.TryGetProperty(primaryKey, out var value)
            || value.ValueKind != global::System.Text.Json.JsonValueKind.String || value.GetString() != id)
        {
            throw Errors.Fail(ErrorCode.Corruption, RetainedRowCorrupt);
        }
    }

    internal static void ValidateLeftIdentity(PartitionRef partition, string collection,
        ReadOnlySpan<byte> key, DocumentRecord record)
    {
        if (record is null || record.Reference is null || record.Reference.Id is null
            || record.Reference.Partition != partition || record.Reference.Collection != collection
            || !key.SequenceEqual(global::KeyLoad.Core.Features.DocumentStorage.DocumentStorageKeys.RecordKey(partition, collection, record.Reference.Id)))
        {
            throw Errors.Fail(ErrorCode.Corruption, RetainedRowCorrupt);
        }
    }

    internal static void ValidateRightIdentity(PartitionRef partition, string collection,
        string id, DocumentRecord record)
    {
        if (record.Reference is null || record.Reference.Partition != partition
            || record.Reference.Collection != collection || record.Reference.Id != id)
        {
            throw Errors.Fail(ErrorCode.Corruption, RetainedRowCorrupt);
        }
    }
}
