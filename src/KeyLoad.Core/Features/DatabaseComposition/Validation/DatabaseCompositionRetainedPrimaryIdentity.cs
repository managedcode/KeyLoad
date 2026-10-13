namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireTypedEndpointPrimaryIdentity(DocumentRecord record, string primaryKey)
    {
        global::System.Text.Json.JsonDocument document;
        try
        {
            document = global::System.Text.Json.JsonDocument.Parse(record.Json,
                new global::System.Text.Json.JsonDocumentOptions { MaxDepth = Limits.MaxJsonDepth });
        }
        catch (global::System.Text.Json.JsonException)
        { throw Errors.Fail(ErrorCode.Corruption, TypedJoinRecordMismatch); }
        using (document)
        {
            var row = document.RootElement;
            if (row.ValueKind != global::System.Text.Json.JsonValueKind.Object)
            { throw Errors.Fail(ErrorCode.Corruption, TypedJoinRecordMismatch); }
            var value = FindUniqueTypedEndpointPrimaryValue(row, primaryKey);
            if (value is not { ValueKind: global::System.Text.Json.JsonValueKind.String } primary
                || !StringComparer.Ordinal.Equals(primary.GetString(), record.Reference.Id))
            { throw Errors.Fail(ErrorCode.Corruption, TypedJoinRecordMismatch); }
        }
    }

    private static global::System.Text.Json.JsonElement? FindUniqueTypedEndpointPrimaryValue(global::System.Text.Json.JsonElement row, string primaryKey)
    {
        global::System.Text.Json.JsonElement? primary = null;
        foreach (var property in row.EnumerateObject())
        {
            if (!property.NameEquals(primaryKey))
            { continue; }
            if (primary is not null)
            { throw Errors.Fail(ErrorCode.Corruption, TypedJoinRecordMismatch); }
            primary = property.Value;
        }
        return primary;
    }
}
