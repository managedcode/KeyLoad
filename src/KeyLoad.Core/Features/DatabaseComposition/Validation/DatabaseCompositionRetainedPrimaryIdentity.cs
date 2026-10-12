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
            if (row.ValueKind != global::System.Text.Json.JsonValueKind.Object
                || !row.TryGetProperty(primaryKey, out var value)
                || value.ValueKind != global::System.Text.Json.JsonValueKind.String
                || !StringComparer.Ordinal.Equals(value.GetString(), record.Reference.Id))
            { throw Errors.Fail(ErrorCode.Corruption, TypedJoinRecordMismatch); }
        }
    }
}
